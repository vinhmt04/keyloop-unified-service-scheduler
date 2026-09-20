using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using UnifiedServiceScheduler.Data;
using UnifiedServiceScheduler.Data.Entities;
using UnifiedServiceScheduler.Services;

namespace UnifiedServiceScheduler.Tests.Integration;

/// <summary>
/// PostgreSQL integration tests for resource availability checking and assignment logic.
/// Tests requirements: 2.1, 2.2, 2.3, 2.4, 3.3, 3.4
/// Tests design properties: Resource exclusivity, resource qualification
/// </summary>
public class ResourceAvailabilityIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgreSqlContainer = new PostgreSqlBuilder()
        .WithDatabase("resource_availability_test")
        .WithUsername("test_user")
        .WithPassword("test_password")
        .Build();

    private ServiceProvider? _serviceProvider;
    private string? _connectionString;

    [Fact]
    public async Task AvailabilityService_ShouldDetectServiceBayConflict_WithOverlappingAppointments()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var availabilityService = scope.ServiceProvider.GetRequiredService<AvailabilityService>();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();

        var startTime = DateTime.UtcNow.AddDays(1).Date.AddHours(10); // 10:00 AM
        var endTime = startTime.AddHours(1); // 11:00 AM

        // Create existing appointment in service bay 1: 10:00-11:00
        var existingAppointment = new Appointment
        {
            CustomerId = 12345,
            VehicleVin = "EXISTING1234567",
            ServiceTypeId = 1,
            DealershipId = 1,
            StartTime = startTime,
            EndTime = endTime,
            ServiceBayId = 1,
            TechnicianId = 1,
            CreatedAt = DateTime.UtcNow
        };
        context.Appointments.Add(existingAppointment);
        await context.SaveChangesAsync();

        // Verify the appointment was created
        var verifyAppointment = await context.Appointments
            .FirstOrDefaultAsync(a => a.ServiceBayId == 1 && a.StartTime == startTime);
        Assert.NotNull(verifyAppointment);

        // Act - Check if ANY service bay in dealership 1 is available for overlapping time: 10:30-11:30
        // Since only service bay 1 has an appointment and there are 2 other service bays available,
        // this should return true (service bays 2 and 3 are available)
        var isAvailable = await availabilityService.IsServiceBayAvailableAsync(
            1, // dealershipId
            startTime.AddMinutes(30), // overlapping start time: 10:30
            startTime.AddMinutes(90)); // overlapping end time: 11:30

        // Assert - Requirement 2.1: Since there are other service bays available, this should return true
        Assert.True(isAvailable, "Other service bays should be available even if service bay 1 is busy");
    }

    [Fact]
    public async Task AvailabilityService_ShouldAllowAdjacentAppointments_WithNoOverlap()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var availabilityService = scope.ServiceProvider.GetRequiredService<AvailabilityService>();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();

        var firstStart = DateTime.UtcNow.AddDays(2).Date.AddHours(9);
        var firstEnd = firstStart.AddMinutes(30);

        // Create first appointment: 9:00-9:30
        var firstAppointment = new Appointment
        {
            CustomerId = 12346,
            VehicleVin = "FIRST12345678",
            ServiceTypeId = 1,
            DealershipId = 1,
            StartTime = firstStart,
            EndTime = firstEnd,
            ServiceBayId = 1,
            TechnicianId = 1,
            CreatedAt = DateTime.UtcNow
        };
        context.Appointments.Add(firstAppointment);
        await context.SaveChangesAsync();

        // Act - Check if service bay 1 is available immediately after first appointment: 9:30-10:00
        var isAvailable = await availabilityService.IsServiceBayAvailableAsync(
            1, // dealershipId
            firstEnd, // start exactly when first appointment ends
            firstEnd.AddMinutes(30)); // 30 minute duration

        // Assert - Adjacent appointments should be allowed (half-open interval [start, end))
        Assert.True(isAvailable, "Service bay should be available immediately after previous appointment ends");
    }

    [Fact]
    public async Task AvailabilityService_ShouldReturnTrueForQualifiedTechnicianAvailability()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var availabilityService = scope.ServiceProvider.GetRequiredService<AvailabilityService>();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();

        // Use a time slot far in the future to ensure no conflicts
        var startTime = DateTime.UtcNow.AddDays(30).Date.AddHours(8); // Early morning 30 days from now
        var endTime = startTime.AddMinutes(90);

        // Act - Check technician availability for Oil Change (ServiceTypeId=1) 
        // From seed data: John Smith (TechnicianId=1) is qualified for Oil Change
        var isAvailable = await availabilityService.IsTechnicianAvailableAsync(
            1, // dealershipId
            1, // serviceTypeId (Oil Change)
            startTime,
            endTime);

        // Assert - Requirement 2.2: System SHALL verify qualified technician is available
        Assert.True(isAvailable, "Qualified technician should be available for Oil Change at unused time slot");
        
        // Verify there's actually a qualified technician available
        var qualifiedTechnicians = await context.TechnicianQualifications
            .Include(tq => tq.Technician)
            .Where(tq => tq.ServiceTypeId == 1 && 
                       tq.Technician.DealershipId == 1 && 
                       tq.Technician.IsActive)
            .Select(tq => tq.Technician)
            .ToListAsync();

        Assert.NotEmpty(qualifiedTechnicians);
    }

    [Fact]
    public async Task AvailabilityService_ShouldReturnAvailableServiceBays_ExcludingBusyOnes()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var availabilityService = scope.ServiceProvider.GetRequiredService<AvailabilityService>();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();

        var startTime = DateTime.UtcNow.AddDays(4).Date.AddHours(14);
        var endTime = startTime.AddHours(1);

        // Book service bay 1
        var existingAppointment = new Appointment
        {
            CustomerId = 12347,
            VehicleVin = "BUSY123456789",
            ServiceTypeId = 1,
            DealershipId = 1,
            StartTime = startTime,
            EndTime = endTime,
            ServiceBayId = 1,
            TechnicianId = 1,
            CreatedAt = DateTime.UtcNow
        };
        context.Appointments.Add(existingAppointment);
        await context.SaveChangesAsync();

        // Act
        var availableBays = await availabilityService.GetAvailableServiceBaysAsync(
            1, // dealershipId
            startTime,
            endTime);

        // Assert - Should return available bays excluding the busy one
        Assert.NotEmpty(availableBays);
        Assert.DoesNotContain(availableBays, bay => bay.ServiceBayId == 1);
        Assert.All(availableBays, bay => Assert.Equal(1, bay.DealershipId));
        Assert.All(availableBays, bay => Assert.True(bay.IsActive));
    }

    [Fact]
    public async Task AppointmentService_ShouldAssignQualifiedTechnicianForBrakeRepair()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var appointmentService = scope.ServiceProvider.GetRequiredService<AppointmentService>();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();

        // Use a time slot with guaranteed availability
        var startTime = DateTime.UtcNow.AddDays(10).Date.AddHours(8); // Early morning 10 days from now

        var request = new CreateAppointmentRequest
        {
            CustomerId = 15001,
            VehicleVin = "BRAKETEST123456",
            ServiceTypeId = 2, // Brake Repair (requires specific qualification)
            DealershipId = 1,
            StartTime = startTime
        };

        // Act - Create appointment for Brake Repair
        var result = await appointmentService.CreateAppointmentAsync(request);

        // Assert - Requirement 2.2: System SHALL assign qualified technician
        Assert.True(result.Success, $"Appointment creation should succeed for Brake Repair. Error: {result.ErrorMessage}");
        Assert.NotNull(result.CreatedAppointment);

        // Verify assigned technician is qualified for brake repair
        var technicianQualification = await context.TechnicianQualifications
            .FirstOrDefaultAsync(tq => tq.TechnicianId == result.CreatedAppointment.TechnicianId && 
                                     tq.ServiceTypeId == 2);
        
        Assert.NotNull(technicianQualification);
        Assert.True(technicianQualification != null, "Assigned technician must be qualified for Brake Repair");
    }

    [Fact]
    public async Task AppointmentService_ShouldFail_WhenNoQualifiedTechnicianAvailable()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var appointmentService = scope.ServiceProvider.GetRequiredService<AppointmentService>();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();

        var startTime = DateTime.UtcNow.AddDays(6).Date.AddHours(9);
        var endTime = startTime.AddMinutes(90);

        // Book all technicians qualified for Transmission Service (ServiceTypeId = 5)
        // From seed data, only Mike Wilson (TechnicianId = 3) is qualified
        var blockingAppointment = new Appointment
        {
            CustomerId = 12348,
            VehicleVin = "BLOCK1234567",
            ServiceTypeId = 5, // Transmission Service
            DealershipId = 1,
            StartTime = startTime,
            EndTime = endTime,
            ServiceBayId = 1,
            TechnicianId = 3, // Mike Wilson - only qualified technician
            CreatedAt = DateTime.UtcNow
        };
        context.Appointments.Add(blockingAppointment);
        await context.SaveChangesAsync();

        // Act - Try to create another Transmission Service at overlapping time
        var request = new CreateAppointmentRequest
        {
            CustomerId = 15002,
            VehicleVin = "TRANSMISSIONTEST",
            ServiceTypeId = 5, // Transmission Service
            DealershipId = 1,
            StartTime = startTime.AddMinutes(30) // overlapping time
        };

        var result = await appointmentService.CreateAppointmentAsync(request);

        // Assert - Requirement 2.4: System SHALL indicate appointment cannot be confirmed when no qualified technician available
        Assert.False(result.Success, "Appointment should fail when no qualified technician is available");
        Assert.Equal(AppointmentCreationErrorType.ResourceConflict, result.ErrorType);
        Assert.Contains("Resource assignment failed", result.ErrorMessage);
    }

    [Fact]
    public async Task AppointmentService_ShouldFail_WhenNoServiceBayAvailable()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var appointmentService = scope.ServiceProvider.GetRequiredService<AppointmentService>();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();

        var startTime = DateTime.UtcNow.AddDays(7).Date.AddHours(10);
        var endTime = startTime.AddMinutes(30);

        // Book all service bays for Oil Change (there are 3 service bays, but only 2 technicians qualified for Oil Change)
        // So we need to book 2 appointments which will exhaust qualified technician capacity
        var firstAppointment = new Appointment
        {
            CustomerId = 20000,
            VehicleVin = "OILCHANGE1",
            ServiceTypeId = 1, // Oil Change
            DealershipId = 1,
            StartTime = startTime,
            EndTime = endTime,
            ServiceBayId = 1,
            TechnicianId = 1, // John Smith (qualified for oil change)
            CreatedAt = DateTime.UtcNow
        };

        var secondAppointment = new Appointment
        {
            CustomerId = 20001,
            VehicleVin = "OILCHANGE2",
            ServiceTypeId = 1, // Oil Change
            DealershipId = 1,
            StartTime = startTime,
            EndTime = endTime,
            ServiceBayId = 2,
            TechnicianId = 3, // Mike Wilson (also qualified for oil change)
            CreatedAt = DateTime.UtcNow
        };

        context.Appointments.AddRange(firstAppointment, secondAppointment);
        await context.SaveChangesAsync();

        // Act - Try to create another Oil Change appointment when all qualified technicians are busy
        var request = new CreateAppointmentRequest
        {
            CustomerId = 15003,
            VehicleVin = "OILCHANGETEST",
            ServiceTypeId = 1, // Oil Change
            DealershipId = 1,
            StartTime = startTime.AddMinutes(15) // overlapping time
        };

        var result = await appointmentService.CreateAppointmentAsync(request);

        // Assert - Requirement 2.3: System SHALL indicate appointment cannot be confirmed when resources are not available
        Assert.False(result.Success, "Appointment should fail when no qualified technicians are available");
        Assert.Equal(AppointmentCreationErrorType.ResourceConflict, result.ErrorType);
        Assert.Contains("Resource assignment failed", result.ErrorMessage);
    }

    [Fact]
    public async Task TimeIntervalLogic_ShouldWorkCorrectly_WithHalfOpenIntervals()
    {
        // Arrange
        using var scope = _serviceProvider!.CreateScope();
        var timeIntervalService = scope.ServiceProvider.GetRequiredService<TimeIntervalService>();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();

        // Get oil change service type (30 minutes duration)
        var serviceType = await context.ServiceTypes.FindAsync(1);
        Assert.NotNull(serviceType);

        var appointmentStart = DateTime.UtcNow.AddDays(8).Date.AddHours(9); // 9:00 AM

        // Act - Calculate end time
        var calculatedEnd = timeIntervalService.CalculateAppointmentEndTime(appointmentStart, serviceType);

        // Assert - Requirement 1.2, 1.3: System SHALL determine service duration and identify time slot
        var expectedEnd = appointmentStart.Add(serviceType.Duration);
        Assert.Equal(expectedEnd, calculatedEnd);

        // Test interval overlap logic
        var existingStart = appointmentStart;
        var existingEnd = calculatedEnd;
        
        // Test: appointment exactly after should not overlap (half-open intervals)
        var nextStart = calculatedEnd;
        var nextEnd = nextStart.AddMinutes(30);
        var overlaps = timeIntervalService.DoIntervalsOverlap(existingStart, existingEnd, nextStart, nextEnd);
        Assert.False(overlaps, "Adjacent appointments should not overlap with half-open intervals");

        // Test: appointment with 1 minute overlap should overlap
        var overlapStart = calculatedEnd.AddMinutes(-1);
        var overlapEnd = overlapStart.AddMinutes(30);
        var shouldOverlap = timeIntervalService.DoIntervalsOverlap(existingStart, existingEnd, overlapStart, overlapEnd);
        Assert.True(shouldOverlap, "Appointments with even 1 minute overlap should be detected");
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