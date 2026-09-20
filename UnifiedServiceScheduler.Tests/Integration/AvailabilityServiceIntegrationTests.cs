using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using UnifiedServiceScheduler.Data;
using UnifiedServiceScheduler.Data.Entities;
using UnifiedServiceScheduler.Services;

namespace UnifiedServiceScheduler.Tests.Integration;

public class AvailabilityServiceIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _dbContainer = null!;
    private ServiceSchedulerDbContext _context = null!;
    private AvailabilityService _availabilityService = null!;

    public async Task InitializeAsync()
    {
        _dbContainer = new PostgreSqlBuilder()
            .WithDatabase("unified_service_scheduler_test")
            .WithUsername("test_user")
            .WithPassword("test_password")
            .Build();

        await _dbContainer.StartAsync();

        var options = new DbContextOptionsBuilder<ServiceSchedulerDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .Options;

        _context = new ServiceSchedulerDbContext(options);
        await _context.Database.EnsureCreatedAsync();

        var timeIntervalService = new TimeIntervalService();
        _availabilityService = new AvailabilityService(_context, timeIntervalService);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _dbContainer.DisposeAsync();
    }

    [Fact]
    public async Task AvailabilityService_WithRealDatabase_ShouldDetectOverlappingAppointments()
    {
        // Arrange
        var dealershipId = 1;
        var serviceTypeId = 1; // Oil Change (exists in seed data)

        // The database already has seed data with service bays and technicians with qualifications
        // Let's add an appointment that overlaps with our requested time
        var conflictingAppointment = new Appointment
        {
            CustomerId = 100,
            VehicleVin = "1HGBH41JXMN109186",
            ServiceTypeId = serviceTypeId,
            DealershipId = dealershipId,
            StartTime = new DateTime(2024, 1, 15, 8, 30, 0, DateTimeKind.Utc),  // 8:30 AM UTC
            EndTime = new DateTime(2024, 1, 15, 9, 30, 0, DateTimeKind.Utc),    // 9:30 AM UTC
            ServiceBayId = 1,    // Using first service bay from seed data
            TechnicianId = 1,    // Using John Smith who is qualified for Oil Change
            CreatedAt = DateTime.UtcNow
        };

        _context.Appointments.Add(conflictingAppointment);
        await _context.SaveChangesAsync();

        var requestedStartTime = new DateTime(2024, 1, 15, 9, 0, 0, DateTimeKind.Utc);   // 9:00 AM UTC - overlaps with existing
        var requestedEndTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);   // 10:00 AM UTC

        // Act
        var availableServiceBays = await _availabilityService.GetAvailableServiceBaysAsync(
            dealershipId, requestedStartTime, requestedEndTime);

        var availableTechnicians = await _availabilityService.GetAvailableTechniciansAsync(
            dealershipId, serviceTypeId, requestedStartTime, requestedEndTime);

        // Assert
        // Service bay 1 should be unavailable (has conflicting appointment)
        // Service bays 2 and 3 should be available (from seed data)
        Assert.Equal(2, availableServiceBays.Count);
        Assert.DoesNotContain(availableServiceBays, sb => sb.ServiceBayId == 1);
        Assert.Contains(availableServiceBays, sb => sb.ServiceBayId == 2);
        Assert.Contains(availableServiceBays, sb => sb.ServiceBayId == 3);

        // John Smith (technician 1) should be unavailable due to the appointment
        // Mike Wilson (technician 3) should be available and qualified for Oil Change
        Assert.Single(availableTechnicians);
        Assert.DoesNotContain(availableTechnicians, t => t.TechnicianId == 1); // John is busy
        Assert.Contains(availableTechnicians, t => t.TechnicianId == 3);       // Mike is available and qualified
    }

    [Fact]
    public async Task AvailabilityService_WithRealDatabase_ShouldRespectQualifications()
    {
        // Arrange
        var dealershipId = 1;
        var brakeRepairServiceTypeId = 2; // Brake Repair (from seed data)

        var requestedStartTime = new DateTime(2024, 1, 15, 14, 0, 0, DateTimeKind.Utc);   // 2:00 PM UTC
        var requestedEndTime = new DateTime(2024, 1, 15, 16, 0, 0, DateTimeKind.Utc);     // 4:00 PM UTC (2 hours for brake repair)

        // Act
        var availableTechnicians = await _availabilityService.GetAvailableTechniciansAsync(
            dealershipId, brakeRepairServiceTypeId, requestedStartTime, requestedEndTime);

        // Assert
        // From seed data, only Sarah Johnson (ID 2) and Mike Wilson (ID 3) are qualified for Brake Repair
        Assert.Equal(2, availableTechnicians.Count);
        Assert.Contains(availableTechnicians, t => t.TechnicianId == 2); // Sarah Johnson
        Assert.Contains(availableTechnicians, t => t.TechnicianId == 3); // Mike Wilson
        Assert.DoesNotContain(availableTechnicians, t => t.TechnicianId == 1); // John Smith not qualified for brake repair
    }

    [Fact]
    public async Task AvailabilityService_WithRealDatabase_ShouldUseHalfOpenIntervalSemantics()
    {
        // Arrange
        var dealershipId = 1;
        var serviceTypeId = 1;

        // Add appointment that ends exactly when our requested appointment starts (should NOT overlap)
        var nonOverlappingAppointment = new Appointment
        {
            CustomerId = 101,
            VehicleVin = "1HGBH41JXMN109187",
            ServiceTypeId = serviceTypeId,
            DealershipId = dealershipId,
            StartTime = new DateTime(2024, 1, 15, 8, 0, 0, DateTimeKind.Utc),   // 8:00 AM UTC
            EndTime = new DateTime(2024, 1, 15, 9, 0, 0, DateTimeKind.Utc),     // 9:00 AM UTC (half-open interval [8:00, 9:00))
            ServiceBayId = 1,
            TechnicianId = 1,
            CreatedAt = DateTime.UtcNow
        };

        _context.Appointments.Add(nonOverlappingAppointment);
        await _context.SaveChangesAsync();

        var requestedStartTime = new DateTime(2024, 1, 15, 9, 0, 0, DateTimeKind.Utc);    // 9:00 AM UTC - should be available
        var requestedEndTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);    // 10:00 AM UTC

        // Act
        var availableServiceBays = await _availabilityService.GetAvailableServiceBaysAsync(
            dealershipId, requestedStartTime, requestedEndTime);

        var availableTechnicians = await _availabilityService.GetAvailableTechniciansAsync(
            dealershipId, serviceTypeId, requestedStartTime, requestedEndTime);

        // Assert
        // All resources should be available because the existing appointment ends exactly when the new one starts
        // This tests the half-open interval semantics: [8:00, 9:00) does NOT overlap with [9:00, 10:00)
        Assert.Equal(3, availableServiceBays.Count); // All 3 service bays available
        Assert.Equal(2, availableTechnicians.Count); // John and Mike are both qualified for Oil Change
    }
}