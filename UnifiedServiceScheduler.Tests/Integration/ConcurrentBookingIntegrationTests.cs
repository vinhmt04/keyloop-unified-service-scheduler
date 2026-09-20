using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Testcontainers.PostgreSql;
using UnifiedServiceScheduler.Data;
using UnifiedServiceScheduler.Data.Entities;
using UnifiedServiceScheduler.Services;

namespace UnifiedServiceScheduler.Tests.Integration;

/// <summary>
/// PostgreSQL integration tests for concurrent booking scenarios and double-booking prevention.
/// Tests requirements: 3.3, 3.4 (double-booking prevention)
/// Tests design properties: Resource exclusivity under concurrent access
/// </summary>
public class ConcurrentBookingIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgreSqlContainer = new PostgreSqlBuilder()
        .WithDatabase("concurrent_booking_test")
        .WithUsername("test_user")
        .WithPassword("test_password")
        .Build();

    private ServiceProvider? _serviceProvider;
    private string? _connectionString;

    [Fact]
    public async Task ConcurrentBookings_ShouldPreventDoubleBooking_WhenTargetingSameTimeSlot()
    {
        // Arrange - Create multiple concurrent requests for the same time slot
        // This will test resource exclusivity when multiple requests compete for limited resources
        var startTime = DateTime.UtcNow.AddDays(5).Date.AddHours(10); // 10:00 AM in 5 days
        const int concurrentRequestCount = 5;
        
        var requests = Enumerable.Range(1, concurrentRequestCount)
            .Select(i => new CreateAppointmentRequest
            {
                CustomerId = 50000 + i,
                VehicleVin = $"CONCURRENT{i:D6}",
                ServiceTypeId = 1, // Oil Change (30 minutes, has qualified technicians)
                DealershipId = 1,
                StartTime = startTime
            })
            .ToList();

        // Act - Execute all requests concurrently using Task.Run for true concurrency
        var results = new ConcurrentBag<CreateAppointmentResult>();
        
        var tasks = requests.Select(async request =>
        {
            // Create a new scope for each concurrent request to simulate independent HTTP requests
            using var scope = _serviceProvider!.CreateScope();
            var appointmentService = scope.ServiceProvider.GetRequiredService<AppointmentService>();
            
            // Use Task.Run to ensure true concurrency on different threads
            var result = await Task.Run(async () => 
                await appointmentService.CreateAppointmentAsync(request));
            
            results.Add(result);
        });

        await Task.WhenAll(tasks);

        // Assert - Verify resource exclusivity in results and database state
        var resultsList = results.ToList();
        var successfulBookings = resultsList.Where(r => r.Success).ToList();
        var failedBookings = resultsList.Where(r => !r.Success).ToList();
        
        // From seed data: Only 2 technicians (John Smith, Mike Wilson) are qualified for Oil Change
        // So maximum 2 concurrent Oil Change appointments should succeed at the same time
        Assert.True(successfulBookings.Count <= 2, 
            $"Expected at most 2 successful bookings due to technician limit, got {successfulBookings.Count}");
        Assert.True(successfulBookings.Count >= 1, 
            "Expected at least 1 successful booking");
        Assert.Equal(concurrentRequestCount, successfulBookings.Count + failedBookings.Count);

        // Verify failed bookings have resource conflict errors
        Assert.All(failedBookings, failedResult =>
        {
            Assert.Equal(AppointmentCreationErrorType.ResourceConflict, failedResult.ErrorType);
            Assert.Contains("Resource assignment failed", failedResult.ErrorMessage);
        });

        // Verify database state - no resource conflicts exist
        using var scope = _serviceProvider!.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();
        
        var appointmentsAtTime = await context.Appointments
            .Where(a => a.DealershipId == 1 && a.StartTime == startTime)
            .ToListAsync();
        
        Assert.Equal(successfulBookings.Count, appointmentsAtTime.Count);

        // Requirement 3.3: Verify no ServiceBayId is double-booked
        var serviceBayIds = appointmentsAtTime.Select(a => a.ServiceBayId).ToList();
        var uniqueServiceBayIds = serviceBayIds.Distinct().ToList();
        Assert.Equal(serviceBayIds.Count, uniqueServiceBayIds.Count);

        // Requirement 3.4: Verify no TechnicianId is double-booked
        var technicianIds = appointmentsAtTime.Select(a => a.TechnicianId).ToList();
        var uniqueTechnicianIds = technicianIds.Distinct().ToList();
        Assert.Equal(technicianIds.Count, uniqueTechnicianIds.Count);
    }

    [Fact]
    public async Task ConcurrentBookings_ShouldPreventDoubleBooking_WithOverlappingTimeSlots()
    {
        // Arrange - Create concurrent requests with overlapping time slots
        var baseStartTime = DateTime.UtcNow.AddDays(6).Date.AddHours(14); // 2:00 PM in 6 days
        
        var overlappingRequests = new List<CreateAppointmentRequest>
        {
            // Request 1: 14:00 - 14:30 (Oil Change)
            new CreateAppointmentRequest
            {
                CustomerId = 60001,
                VehicleVin = "OVERLAP001",
                ServiceTypeId = 1, // Oil Change (30 minutes)
                DealershipId = 1,
                StartTime = baseStartTime
            },
            
            // Request 2: 14:15 - 14:45 (Oil Change) - Overlaps with first by 15 minutes
            new CreateAppointmentRequest
            {
                CustomerId = 60002,
                VehicleVin = "OVERLAP002", 
                ServiceTypeId = 1, // Oil Change (30 minutes)
                DealershipId = 1,
                StartTime = baseStartTime.AddMinutes(15)
            },
            
            // Request 3: 14:25 - 16:25 (Brake Repair) - Overlaps with both above
            new CreateAppointmentRequest
            {
                CustomerId = 60003,
                VehicleVin = "OVERLAP003",
                ServiceTypeId = 2, // Brake Repair (2 hours) 
                DealershipId = 1,
                StartTime = baseStartTime.AddMinutes(25)
            }
        };

        // Act - Execute overlapping requests concurrently
        var results = new ConcurrentBag<CreateAppointmentResult>();
        
        var tasks = overlappingRequests.Select(async request =>
        {
            using var scope = _serviceProvider!.CreateScope();
            var appointmentService = scope.ServiceProvider.GetRequiredService<AppointmentService>();
            
            var result = await Task.Run(async () =>
                await appointmentService.CreateAppointmentAsync(request));
            
            results.Add(result);
        });

        await Task.WhenAll(tasks);

        // Assert - Verify resource exclusivity prevents overlapping bookings
        var resultsList = results.ToList();
        var successfulBookings = resultsList.Where(r => r.Success).ToList();
        var failedBookings = resultsList.Where(r => !r.Success).ToList();

        // At least one booking should succeed, and overlapping ones should be prevented
        Assert.True(successfulBookings.Count >= 1, "At least one booking should succeed");
        Assert.True(failedBookings.Count >= 1, "Overlapping bookings should be prevented");
        
        // Verify database state shows no overlapping resource conflicts
        using var scope = _serviceProvider!.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();
        
        var allAppointmentsInTimeRange = await context.Appointments
            .Where(a => a.DealershipId == 1)
            .Where(a => a.StartTime >= baseStartTime && a.StartTime < baseStartTime.AddHours(3))
            .ToListAsync();

        // Verify no ServiceBayId conflicts with overlapping times using half-open interval logic
        foreach (var appointment1 in allAppointmentsInTimeRange)
        {
            foreach (var appointment2 in allAppointmentsInTimeRange)
            {
                if (appointment1.AppointmentId != appointment2.AppointmentId)
                {
                    // Check ServiceBay conflicts - Requirement 3.3
                    if (appointment1.ServiceBayId == appointment2.ServiceBayId)
                    {
                        var intervalsOverlap = appointment1.StartTime < appointment2.EndTime && 
                                             appointment2.StartTime < appointment1.EndTime;
                        Assert.False(intervalsOverlap, 
                            $"ServiceBay {appointment1.ServiceBayId} has overlapping appointments: " +
                            $"[{appointment1.StartTime:HH:mm}, {appointment1.EndTime:HH:mm}) and " +
                            $"[{appointment2.StartTime:HH:mm}, {appointment2.EndTime:HH:mm})");
                    }
                    
                    // Check Technician conflicts - Requirement 3.4
                    if (appointment1.TechnicianId == appointment2.TechnicianId)
                    {
                        var intervalsOverlap = appointment1.StartTime < appointment2.EndTime && 
                                             appointment2.StartTime < appointment1.EndTime;
                        Assert.False(intervalsOverlap,
                            $"Technician {appointment1.TechnicianId} has overlapping appointments: " +
                            $"[{appointment1.StartTime:HH:mm}, {appointment1.EndTime:HH:mm}) and " +
                            $"[{appointment2.StartTime:HH:mm}, {appointment2.EndTime:HH:mm})");
                    }
                }
            }
        }
    }

    [Fact]
    public async Task ConcurrentBookings_ShouldSucceed_WhenTargetingDifferentTimeSlots()
    {
        // Arrange - Create concurrent requests for non-overlapping time slots
        // This verifies the system allows valid concurrent bookings when no resource conflicts exist
        var baseDate = DateTime.UtcNow.AddDays(7).Date;
        
        var nonOverlappingRequests = new List<CreateAppointmentRequest>
        {
            // Request 1: 09:00 - 09:30 (Oil Change)
            new CreateAppointmentRequest
            {
                CustomerId = 70001,
                VehicleVin = "SEPARATE001",
                ServiceTypeId = 1, // Oil Change (30 minutes)
                DealershipId = 1,
                StartTime = baseDate.AddHours(9)
            },
            
            // Request 2: 10:00 - 10:30 (Oil Change) - No overlap (half-open intervals)
            new CreateAppointmentRequest
            {
                CustomerId = 70002,
                VehicleVin = "SEPARATE002",
                ServiceTypeId = 1, // Oil Change (30 minutes)
                DealershipId = 1,
                StartTime = baseDate.AddHours(10)
            },
            
            // Request 3: 11:00 - 13:00 (Brake Repair) - No overlap
            new CreateAppointmentRequest
            {
                CustomerId = 70003,
                VehicleVin = "SEPARATE003", 
                ServiceTypeId = 2, // Brake Repair (2 hours)
                DealershipId = 1,
                StartTime = baseDate.AddHours(11)
            }
        };

        // Act - Execute non-overlapping requests concurrently
        var results = new ConcurrentBag<CreateAppointmentResult>();
        
        var tasks = nonOverlappingRequests.Select(async request =>
        {
            using var scope = _serviceProvider!.CreateScope();
            var appointmentService = scope.ServiceProvider.GetRequiredService<AppointmentService>();
            
            var result = await Task.Run(async () =>
                await appointmentService.CreateAppointmentAsync(request));
            
            results.Add(result);
        });

        await Task.WhenAll(tasks);

        // Assert - All non-overlapping requests should succeed
        var resultsList = results.ToList();
        var successfulBookings = resultsList.Where(r => r.Success).ToList();
        var failedBookings = resultsList.Where(r => !r.Success).ToList();

        Assert.Equal(3, successfulBookings.Count);
        Assert.Empty(failedBookings);

        // Verify database state - all appointments properly persisted with resource exclusivity
        using var scope = _serviceProvider!.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();
        
        var createdAppointments = await context.Appointments
            .Where(a => a.DealershipId == 1)
            .Where(a => a.StartTime >= baseDate.AddHours(8) && a.StartTime < baseDate.AddHours(14))
            .ToListAsync();
        
        Assert.Equal(3, createdAppointments.Count);

        // Verify no resource conflicts exist in final database state
        foreach (var appointment1 in createdAppointments)
        {
            foreach (var appointment2 in createdAppointments)
            {
                if (appointment1.AppointmentId != appointment2.AppointmentId)
                {
                    // Requirement 3.3: No ServiceBayId conflicts
                    if (appointment1.ServiceBayId == appointment2.ServiceBayId)
                    {
                        var intervalsOverlap = appointment1.StartTime < appointment2.EndTime && 
                                             appointment2.StartTime < appointment1.EndTime;
                        Assert.False(intervalsOverlap, 
                            "ServiceBay should not have overlapping appointments in non-conflicting scenario");
                    }
                    
                    // Requirement 3.4: No TechnicianId conflicts  
                    if (appointment1.TechnicianId == appointment2.TechnicianId)
                    {
                        var intervalsOverlap = appointment1.StartTime < appointment2.EndTime && 
                                             appointment2.StartTime < appointment1.EndTime;
                        Assert.False(intervalsOverlap,
                            "Technician should not have overlapping appointments in non-conflicting scenario");
                    }
                }
            }
        }
    }

    public async Task InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync();
        _connectionString = _postgreSqlContainer.GetConnectionString();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        
        serviceCollection.AddDbContext<ServiceSchedulerDbContext>(options =>
            options.UseNpgsql(_connectionString));

        // Register services
        serviceCollection.AddScoped<AppointmentService>();
        serviceCollection.AddScoped<ResourceAssignmentService>();
        serviceCollection.AddScoped<AvailabilityService>();
        serviceCollection.AddScoped<TimeIntervalService>();

        _serviceProvider = serviceCollection.BuildServiceProvider();

        // Initialize database with schema and seed data
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _serviceProvider?.Dispose();
        await _postgreSqlContainer.DisposeAsync();
    }
}
