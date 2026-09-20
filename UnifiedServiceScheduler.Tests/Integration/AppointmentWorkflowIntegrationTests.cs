using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using UnifiedServiceScheduler.Data;
using UnifiedServiceScheduler.Data.Entities;
using UnifiedServiceScheduler.Services;

namespace UnifiedServiceScheduler.Tests.Integration;

/// <summary>
/// PostgreSQL integration tests for complete appointment booking workflow.
/// Tests requirements: 2.1, 2.2, 3.1, 3.2, 3.3, 3.4, 3.5
/// Tests design properties: Resource exclusivity, appointment completeness, resource qualification, temporal consistency, atomic resource assignment
/// </summary>
public class AppointmentWorkflowIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgreSqlContainer = new PostgreSqlBuilder()
        .WithDatabase("appointment_workflow_test")
        .WithUsername("test_user")
        .WithPassword("test_password")
        .Build();

    private ServiceProvider? _serviceProvider;
    private string? _connectionString;

    [Fact]
    public async Task CreateAppointment_ShouldSucceed_WhenResourcesAreAvailable()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var appointmentService = scope.ServiceProvider.GetRequiredService<AppointmentService>();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();

        var request = new CreateAppointmentRequest
        {
            CustomerId = 12345,
            VehicleVin = "1HGBH41JXMN109186",
            ServiceTypeId = 1, // Oil Change (30 minutes)
            DealershipId = 1,
            StartTime = DateTime.UtcNow.AddDays(1).Date.AddHours(9) // 9:00 AM tomorrow
        };

        // Act
        var result = await appointmentService.CreateAppointmentAsync(request);

        // Assert - Requirement 3.1: System SHALL create confirmed appointment when resources are available
        Assert.True(result.Success, $"Appointment creation should succeed. Error: {result.ErrorMessage}");
        Assert.NotNull(result.CreatedAppointment);
        Assert.Equal(request.CustomerId, result.CreatedAppointment.CustomerId);
        Assert.Equal(request.VehicleVin, result.CreatedAppointment.VehicleVin);
        Assert.Equal(request.ServiceTypeId, result.CreatedAppointment.ServiceTypeId);
        Assert.Equal(request.DealershipId, result.CreatedAppointment.DealershipId);
        Assert.Equal(request.StartTime, result.CreatedAppointment.StartTime);

        // Requirement 1.2, 1.3: System SHALL determine service duration and identify time slot
        var serviceType = await context.ServiceTypes.FindAsync(request.ServiceTypeId);
        var expectedEndTime = request.StartTime.Add(serviceType!.Duration);
        Assert.Equal(expectedEndTime, result.CreatedAppointment.EndTime);

        // Requirement 3.2: System SHALL associate appointment with specific service bay and technician
        Assert.True(result.CreatedAppointment.ServiceBayId > 0, "Service bay must be assigned");
        Assert.True(result.CreatedAppointment.TechnicianId > 0, "Technician must be assigned");

        // Verify appointment is persisted in database - Requirement 3.5
        var persistedAppointment = await context.Appointments
            .FirstOrDefaultAsync(a => a.AppointmentId == result.CreatedAppointment.AppointmentId);
        Assert.NotNull(persistedAppointment);
        Assert.Equal(result.CreatedAppointment.CustomerId, persistedAppointment.CustomerId);
        Assert.Equal(result.CreatedAppointment.ServiceBayId, persistedAppointment.ServiceBayId);
        Assert.Equal(result.CreatedAppointment.TechnicianId, persistedAppointment.TechnicianId);
    }

    [Fact]
    public async Task CreateAppointment_ShouldSucceedAndAssignQualifiedTechnician_ForBrakeRepair()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var appointmentService = scope.ServiceProvider.GetRequiredService<AppointmentService>();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();

        // Ensure we have a qualified technician available by using a known good time slot
        var request = new CreateAppointmentRequest
        {
            CustomerId = 12346,
            VehicleVin = "2HGBH41JXMN109187",
            ServiceTypeId = 2, // Brake Repair (requires qualification)
            DealershipId = 1,
            StartTime = DateTime.UtcNow.AddDays(7).Date.AddHours(8) // Early morning on a future date to avoid conflicts
        };

        // Act
        var result = await appointmentService.CreateAppointmentAsync(request);

        // Assert - Requirement 2.2: System SHALL verify qualified technician is available and assign them
        Assert.True(result.Success, $"Appointment creation should succeed when qualified technician is available. Error: {result.ErrorMessage}");
        Assert.NotNull(result.CreatedAppointment);
        
        // Verify the assigned technician has qualification for the service type
        var technicianQualification = await context.TechnicianQualifications
            .FirstOrDefaultAsync(tq => tq.TechnicianId == result.CreatedAppointment.TechnicianId 
                                    && tq.ServiceTypeId == request.ServiceTypeId);
        
        Assert.NotNull(technicianQualification);
        Assert.True(technicianQualification != null, 
            "Assigned technician must have qualification for the requested service type");
        Assert.True(result.CreatedAppointment.TechnicianId > 0, "Qualified technician must be assigned");
    }

    [Fact]
    public async Task CreateAppointment_ShouldFail_WhenServiceTypeNotFound()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var appointmentService = scope.ServiceProvider.GetRequiredService<AppointmentService>();

        var request = new CreateAppointmentRequest
        {
            CustomerId = 12347,
            VehicleVin = "3HGBH41JXMN109188",
            ServiceTypeId = 999, // Non-existent service type
            DealershipId = 1,
            StartTime = DateTime.UtcNow.AddDays(1).Date.AddHours(11)
        };

        // Act
        var result = await appointmentService.CreateAppointmentAsync(request);

        // Assert
        Assert.False(result.Success, "Appointment creation should fail for non-existent service type");
        Assert.Equal(AppointmentCreationErrorType.ServiceTypeNotFound, result.ErrorType);
        Assert.Contains("Service type with ID 999 not found", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateAppointment_ShouldFail_WhenInvalidVehicleVin()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var appointmentService = scope.ServiceProvider.GetRequiredService<AppointmentService>();

        var request = new CreateAppointmentRequest
        {
            CustomerId = 12348,
            VehicleVin = "", // Invalid VIN
            ServiceTypeId = 1,
            DealershipId = 1,
            StartTime = DateTime.UtcNow.AddDays(1).Date.AddHours(12)
        };

        // Act
        var result = await appointmentService.CreateAppointmentAsync(request);

        // Assert - Requirement 1.1: System SHALL accept valid appointment request (reject invalid)
        Assert.False(result.Success, "Appointment creation should fail for invalid VIN");
        Assert.Equal(AppointmentCreationErrorType.ValidationFailure, result.ErrorType);
        Assert.Contains("Vehicle VIN must be provided", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateAppointment_ShouldFail_WhenVinTooLong()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var appointmentService = scope.ServiceProvider.GetRequiredService<AppointmentService>();

        var request = new CreateAppointmentRequest
        {
            CustomerId = 12349,
            VehicleVin = "1HGBH41JXMN1091862", // 18 characters (too long)
            ServiceTypeId = 1,
            DealershipId = 1,
            StartTime = DateTime.UtcNow.AddDays(1).Date.AddHours(13)
        };

        // Act
        var result = await appointmentService.CreateAppointmentAsync(request);

        // Assert
        Assert.False(result.Success, "Appointment creation should fail for VIN exceeding 17 characters");
        Assert.Equal(AppointmentCreationErrorType.ValidationFailure, result.ErrorType);
        Assert.Contains("cannot exceed 17 characters", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateAppointment_ShouldPreventDoubleBooking_OfServiceBay()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var appointmentService = scope.ServiceProvider.GetRequiredService<AppointmentService>();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();

        var startTime = DateTime.UtcNow.AddDays(2).Date.AddHours(9); // 9:00 AM day after tomorrow

        // From seed data: Only John Smith (ID=1) and Mike Wilson (ID=3) are qualified for Oil Change
        // This means only 2 of the 3 service bays can be used simultaneously for Oil Change
        
        // Book the first 2 appointments (should succeed)
        var firstRequest = new CreateAppointmentRequest
        {
            CustomerId = 12350,
            VehicleVin = "VIN000000012350",
            ServiceTypeId = 1, // Oil Change (30 minutes)
            DealershipId = 1,
            StartTime = startTime
        };
        
        var firstResult = await appointmentService.CreateAppointmentAsync(firstRequest);
        Assert.True(firstResult.Success, $"First Oil Change appointment should succeed. Error: {firstResult.ErrorMessage}");

        var secondRequest = new CreateAppointmentRequest
        {
            CustomerId = 12351,
            VehicleVin = "VIN000000012351",
            ServiceTypeId = 1, // Oil Change (30 minutes)
            DealershipId = 1,
            StartTime = startTime
        };
        
        var secondResult = await appointmentService.CreateAppointmentAsync(secondRequest);
        Assert.True(secondResult.Success, $"Second Oil Change appointment should succeed. Error: {secondResult.ErrorMessage}");

        // Verify 2 appointments are booked with different technicians and service bays
        var bookedAppointments = await context.Appointments
            .Where(a => a.DealershipId == 1 && a.StartTime == startTime)
            .ToListAsync();
        
        Assert.Equal(2, bookedAppointments.Count);
        
        // Different service bays
        Assert.NotEqual(bookedAppointments[0].ServiceBayId, bookedAppointments[1].ServiceBayId);
        
        // Different technicians (both qualified for Oil Change: John Smith=1, Mike Wilson=3)
        Assert.NotEqual(bookedAppointments[0].TechnicianId, bookedAppointments[1].TechnicianId);
        Assert.Contains(bookedAppointments[0].TechnicianId, new[] { 1, 3 });
        Assert.Contains(bookedAppointments[1].TechnicianId, new[] { 1, 3 });

        // Now attempt to create a third overlapping Oil Change appointment - this should fail
        // because no qualified technicians are available (John and Mike are busy)
        var conflictingRequest = new CreateAppointmentRequest
        {
            CustomerId = 99999,
            VehicleVin = "CONFLICTVIN123",
            ServiceTypeId = 1, // Same service type
            DealershipId = 1,
            StartTime = startTime.AddMinutes(15) // Overlapping time
        };

        // Act - Try to create overlapping appointment when all qualified technicians are busy
        var conflictResult = await appointmentService.CreateAppointmentAsync(conflictingRequest);

        // Assert - Requirement 2.3: System SHALL indicate appointment cannot be confirmed when no qualified technician available
        Assert.False(conflictResult.Success, "Appointment should fail when all qualified technicians are busy");
        Assert.Equal(AppointmentCreationErrorType.ResourceConflict, conflictResult.ErrorType);
        Assert.Contains("Resource assignment failed", conflictResult.ErrorMessage);
    }

    [Fact]
    public async Task GetAppointmentById_ShouldReturnAppointment_WhenExists()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var appointmentService = scope.ServiceProvider.GetRequiredService<AppointmentService>();

        var request = new CreateAppointmentRequest
        {
            CustomerId = 12352,
            VehicleVin = "6HGBH41JXMN109191",
            ServiceTypeId = 1,
            DealershipId = 1,
            StartTime = DateTime.UtcNow.AddDays(3).Date.AddHours(9)
        };

        var createResult = await appointmentService.CreateAppointmentAsync(request);
        Assert.True(createResult.Success);

        // Act
        var retrievedAppointment = await appointmentService.GetAppointmentByIdAsync(
            createResult.CreatedAppointment!.AppointmentId);

        // Assert - Requirement 3.5: System SHALL persist appointment with all associated entities
        Assert.NotNull(retrievedAppointment);
        Assert.Equal(createResult.CreatedAppointment.AppointmentId, retrievedAppointment.AppointmentId);
        Assert.Equal(request.CustomerId, retrievedAppointment.CustomerId);
        Assert.Equal(request.VehicleVin, retrievedAppointment.VehicleVin);
        Assert.Equal(request.ServiceTypeId, retrievedAppointment.ServiceTypeId);
        Assert.Equal(request.DealershipId, retrievedAppointment.DealershipId);
        Assert.Equal(createResult.CreatedAppointment.ServiceBayId, retrievedAppointment.ServiceBayId);
        Assert.Equal(createResult.CreatedAppointment.TechnicianId, retrievedAppointment.TechnicianId);
    }

    [Fact]
    public async Task GetAppointmentById_ShouldReturnNull_WhenNotExists()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var appointmentService = scope.ServiceProvider.GetRequiredService<AppointmentService>();

        // Act
        var retrievedAppointment = await appointmentService.GetAppointmentByIdAsync(99999);

        // Assert
        Assert.Null(retrievedAppointment);
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

        // Initialize database
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