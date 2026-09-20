using Microsoft.EntityFrameworkCore;
using UnifiedServiceScheduler.Data;
using UnifiedServiceScheduler.Data.Entities;

namespace UnifiedServiceScheduler.Services;

/// <summary>
/// Service for checking resource availability for appointment scheduling.
/// Validates service bay and technician availability using interval overlap detection.
/// </summary>
public class AvailabilityService
{
    private readonly ServiceSchedulerDbContext _context;
    private readonly TimeIntervalService _timeIntervalService;

    public AvailabilityService(ServiceSchedulerDbContext context, TimeIntervalService timeIntervalService)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeIntervalService = timeIntervalService ?? throw new ArgumentNullException(nameof(timeIntervalService));
    }

    /// <summary>
    /// Checks if at least one service bay is available for the requested time interval at the specified dealership.
    /// </summary>
    /// <param name="dealershipId">The dealership to check availability for</param>
    /// <param name="requestedStartTime">Start time of the requested time interval</param>
    /// <param name="requestedEndTime">End time of the requested time interval</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if at least one service bay is available, false otherwise</returns>
    public async Task<bool> IsServiceBayAvailableAsync(int dealershipId, DateTime requestedStartTime, 
        DateTime requestedEndTime, CancellationToken cancellationToken = default)
    {
        var availableBays = await GetAvailableServiceBaysAsync(dealershipId, requestedStartTime, requestedEndTime, cancellationToken);
        return availableBays.Any();
    }

    /// <summary>
    /// Gets all available service bays for the requested time interval at the specified dealership.
    /// </summary>
    /// <param name="dealershipId">The dealership to check availability for</param>
    /// <param name="requestedStartTime">Start time of the requested time interval</param>
    /// <param name="requestedEndTime">End time of the requested time interval</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of available service bay candidates</returns>
    public async Task<List<ServiceBay>> GetAvailableServiceBaysAsync(int dealershipId, DateTime requestedStartTime, 
        DateTime requestedEndTime, CancellationToken cancellationToken = default)
    {
        // Find all active service bays at the dealership
        var allServiceBays = await _context.ServiceBays
            .Where(sb => sb.DealershipId == dealershipId && sb.IsActive)
            .ToListAsync(cancellationToken);

        var availableBays = new List<ServiceBay>();

        foreach (var serviceBay in allServiceBays)
        {
            // Check if this service bay has any conflicting appointments using raw SQL overlap condition
            var hasConflictingAppointments = await _context.Appointments
                .Where(a => a.ServiceBayId == serviceBay.ServiceBayId)
                .Where(a => a.StartTime < requestedEndTime && requestedStartTime < a.EndTime)
                .AnyAsync(cancellationToken);

            if (!hasConflictingAppointments)
            {
                availableBays.Add(serviceBay);
            }
        }

        return availableBays;
    }

    /// <summary>
    /// Checks if at least one qualified technician is available for the requested service type and time interval at the specified dealership.
    /// </summary>
    /// <param name="dealershipId">The dealership to check availability for</param>
    /// <param name="serviceTypeId">The service type requiring qualification</param>
    /// <param name="requestedStartTime">Start time of the requested time interval</param>
    /// <param name="requestedEndTime">End time of the requested time interval</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if at least one qualified technician is available, false otherwise</returns>
    public async Task<bool> IsTechnicianAvailableAsync(int dealershipId, int serviceTypeId, DateTime requestedStartTime, 
        DateTime requestedEndTime, CancellationToken cancellationToken = default)
    {
        var availableTechnicians = await GetAvailableTechniciansAsync(dealershipId, serviceTypeId, requestedStartTime, requestedEndTime, cancellationToken);
        return availableTechnicians.Any();
    }

    /// <summary>
    /// Gets all qualified technicians available for the requested service type and time interval at the specified dealership.
    /// </summary>
    /// <param name="dealershipId">The dealership to check availability for</param>
    /// <param name="serviceTypeId">The service type requiring qualification</param>
    /// <param name="requestedStartTime">Start time of the requested time interval</param>
    /// <param name="requestedEndTime">End time of the requested time interval</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of available qualified technician candidates</returns>
    public async Task<List<Technician>> GetAvailableTechniciansAsync(int dealershipId, int serviceTypeId, 
        DateTime requestedStartTime, DateTime requestedEndTime, CancellationToken cancellationToken = default)
    {
        // Find all active technicians at the dealership who are qualified for the service type
        var qualifiedTechnicians = await _context.Technicians
            .Where(t => t.DealershipId == dealershipId && t.IsActive)
            .Where(t => t.TechnicianQualifications.Any(tq => tq.ServiceTypeId == serviceTypeId))
            .ToListAsync(cancellationToken);

        var availableTechnicians = new List<Technician>();

        foreach (var technician in qualifiedTechnicians)
        {
            // Check if this technician has any conflicting appointments using raw SQL overlap condition
            var hasConflictingAppointments = await _context.Appointments
                .Where(a => a.TechnicianId == technician.TechnicianId)
                .Where(a => a.StartTime < requestedEndTime && requestedStartTime < a.EndTime)
                .AnyAsync(cancellationToken);

            if (!hasConflictingAppointments)
            {
                availableTechnicians.Add(technician);
            }
        }

        return availableTechnicians;
    }

    /// <summary>
    /// Checks if both a service bay and qualified technician are available for the requested parameters.
    /// This is a combined availability check for convenience.
    /// </summary>
    /// <param name="dealershipId">The dealership to check availability for</param>
    /// <param name="serviceTypeId">The service type requiring qualification</param>
    /// <param name="requestedStartTime">Start time of the requested time interval</param>
    /// <param name="requestedEndTime">End time of the requested time interval</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if both service bay and qualified technician are available, false otherwise</returns>
    public async Task<bool> AreResourcesAvailableAsync(int dealershipId, int serviceTypeId, DateTime requestedStartTime, 
        DateTime requestedEndTime, CancellationToken cancellationToken = default)
    {
        var serviceBayAvailable = await IsServiceBayAvailableAsync(dealershipId, requestedStartTime, requestedEndTime, cancellationToken);
        if (!serviceBayAvailable)
        {
            return false;
        }

        var technicianAvailable = await IsTechnicianAvailableAsync(dealershipId, serviceTypeId, requestedStartTime, requestedEndTime, cancellationToken);
        return technicianAvailable;
    }

    /// <summary>
    /// Gets both available service bays and qualified technicians for the requested parameters.
    /// Returns the candidate resources that can be used for resource assignment.
    /// </summary>
    /// <param name="dealershipId">The dealership to check availability for</param>
    /// <param name="serviceTypeId">The service type requiring qualification</param>
    /// <param name="requestedStartTime">Start time of the requested time interval</param>
    /// <param name="requestedEndTime">End time of the requested time interval</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple containing lists of available service bays and technicians</returns>
    public async Task<(List<ServiceBay> ServiceBays, List<Technician> Technicians)> GetAvailableResourcesAsync(
        int dealershipId, int serviceTypeId, DateTime requestedStartTime, DateTime requestedEndTime, 
        CancellationToken cancellationToken = default)
    {
        var serviceBaysTask = GetAvailableServiceBaysAsync(dealershipId, requestedStartTime, requestedEndTime, cancellationToken);
        var techniciansTask = GetAvailableTechniciansAsync(dealershipId, serviceTypeId, requestedStartTime, requestedEndTime, cancellationToken);

        await Task.WhenAll(serviceBaysTask, techniciansTask);

        return (await serviceBaysTask, await techniciansTask);
    }
}