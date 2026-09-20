using Microsoft.EntityFrameworkCore;
using UnifiedServiceScheduler.Data;
using UnifiedServiceScheduler.Data.Entities;

namespace UnifiedServiceScheduler.Services;

/// <summary>
/// Result of resource assignment operation
/// </summary>
public class ResourceAssignmentResult
{
    public bool Success { get; set; }
    public ServiceBay? AssignedServiceBay { get; set; }
    public Technician? AssignedTechnician { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Service responsible for atomic resource assignment with proper locking.
/// Implements the transactional workflow: identify candidates -> lock selected resources -> recheck availability -> assign resources.
/// </summary>
public class ResourceAssignmentService
{
    private readonly ServiceSchedulerDbContext _context;

    public ResourceAssignmentService(ServiceSchedulerDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Assigns resources (service bay and technician) for an appointment using proper transaction locking.
    /// This method must be called within an active database transaction for proper concurrency control.
    /// </summary>
    /// <param name="dealershipId">The dealership to assign resources in</param>
    /// <param name="serviceTypeId">The service type requiring qualification</param>
    /// <param name="requestedStartTime">Start time of the appointment</param>
    /// <param name="requestedEndTime">End time of the appointment</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resource assignment result with assigned resources or failure reason</returns>
    public async Task<ResourceAssignmentResult> AssignResourcesAsync(int dealershipId, int serviceTypeId, 
        DateTime requestedStartTime, DateTime requestedEndTime, CancellationToken cancellationToken = default)
    {
        // Verify we're in an active transaction for proper concurrency control
        if (_context.Database.CurrentTransaction == null)
        {
            return new ResourceAssignmentResult
            {
                Success = false,
                ErrorMessage = "Resource assignment must be called within an active database transaction"
            };
        }

        // Step 1: Identify candidate resources (no locking yet)
        var candidateServiceBays = await GetCandidateServiceBaysAsync(dealershipId, requestedStartTime, requestedEndTime, cancellationToken);
        var candidateTechnicians = await GetCandidateTechniciansAsync(dealershipId, serviceTypeId, requestedStartTime, requestedEndTime, cancellationToken);

        // Check if we have any candidates
        if (!candidateServiceBays.Any())
        {
            return new ResourceAssignmentResult
            {
                Success = false,
                ErrorMessage = "No service bay available for the requested time slot"
            };
        }

        if (!candidateTechnicians.Any())
        {
            return new ResourceAssignmentResult
            {
                Success = false,
                ErrorMessage = "No qualified technician available for the requested time slot"
            };
        }

        // Step 2: Select resources deterministically (lowest ID first for predictable behavior)
        var selectedServiceBay = candidateServiceBays.OrderBy(sb => sb.ServiceBayId).First();
        var selectedTechnician = candidateTechnicians.OrderBy(t => t.TechnicianId).First();

        // Step 3: Lock resources in consistent order (ServiceBay first, then Technician) to prevent deadlocks
        var lockedServiceBay = await LockServiceBayAsync(selectedServiceBay.ServiceBayId, cancellationToken);
        if (lockedServiceBay == null)
        {
            return new ResourceAssignmentResult
            {
                Success = false,
                ErrorMessage = "Failed to lock selected service bay"
            };
        }

        var lockedTechnician = await LockTechnicianAsync(selectedTechnician.TechnicianId, cancellationToken);
        if (lockedTechnician == null)
        {
            return new ResourceAssignmentResult
            {
                Success = false,
                ErrorMessage = "Failed to lock selected technician"
            };
        }

        // Step 4: Recheck availability while resources are locked
        var serviceBayStillAvailable = await IsServiceBayAvailableWhileLockedAsync(selectedServiceBay.ServiceBayId, 
            requestedStartTime, requestedEndTime, cancellationToken);
        
        if (!serviceBayStillAvailable)
        {
            return new ResourceAssignmentResult
            {
                Success = false,
                ErrorMessage = "Selected service bay became unavailable during resource locking"
            };
        }

        var technicianStillAvailable = await IsTechnicianAvailableWhileLockedAsync(selectedTechnician.TechnicianId, 
            requestedStartTime, requestedEndTime, cancellationToken);
        
        if (!technicianStillAvailable)
        {
            return new ResourceAssignmentResult
            {
                Success = false,
                ErrorMessage = "Selected technician became unavailable during resource locking"
            };
        }

        // Step 5: Resources are successfully assigned (locked and verified available)
        return new ResourceAssignmentResult
        {
            Success = true,
            AssignedServiceBay = lockedServiceBay,
            AssignedTechnician = lockedTechnician
        };
    }

    /// <summary>
    /// Gets candidate service bays for the requested time interval.
    /// Uses standard queries without locking for initial resource identification.
    /// </summary>
    /// <param name="dealershipId">The dealership to check</param>
    /// <param name="requestedStartTime">Start time of the requested interval</param>
    /// <param name="requestedEndTime">End time of the requested interval</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of candidate service bays</returns>
    private async Task<List<ServiceBay>> GetCandidateServiceBaysAsync(int dealershipId, DateTime requestedStartTime, 
        DateTime requestedEndTime, CancellationToken cancellationToken)
    {
        // Find all active service bays at the dealership
        var allServiceBays = await _context.ServiceBays
            .Where(sb => sb.DealershipId == dealershipId && sb.IsActive)
            .ToListAsync(cancellationToken);

        var candidateBays = new List<ServiceBay>();

        foreach (var serviceBay in allServiceBays)
        {
            // Check if this service bay has any conflicting appointments using half-open interval overlap
            var hasConflictingAppointments = await _context.Appointments
                .Where(a => a.ServiceBayId == serviceBay.ServiceBayId)
                .Where(a => a.StartTime < requestedEndTime && requestedStartTime < a.EndTime)
                .AnyAsync(cancellationToken);

            if (!hasConflictingAppointments)
            {
                candidateBays.Add(serviceBay);
            }
        }

        return candidateBays;
    }

    /// <summary>
    /// Gets candidate technicians qualified for the service type and available for the requested time interval.
    /// Uses standard queries without locking for initial resource identification.
    /// </summary>
    /// <param name="dealershipId">The dealership to check</param>
    /// <param name="serviceTypeId">The service type requiring qualification</param>
    /// <param name="requestedStartTime">Start time of the requested interval</param>
    /// <param name="requestedEndTime">End time of the requested interval</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of candidate technicians</returns>
    private async Task<List<Technician>> GetCandidateTechniciansAsync(int dealershipId, int serviceTypeId, 
        DateTime requestedStartTime, DateTime requestedEndTime, CancellationToken cancellationToken)
    {
        // Find all active technicians at the dealership who are qualified for the service type
        var qualifiedTechnicians = await _context.Technicians
            .Where(t => t.DealershipId == dealershipId && t.IsActive)
            .Where(t => t.TechnicianQualifications.Any(tq => tq.ServiceTypeId == serviceTypeId))
            .ToListAsync(cancellationToken);

        var candidateTechnicians = new List<Technician>();

        foreach (var technician in qualifiedTechnicians)
        {
            // Check if this technician has any conflicting appointments using half-open interval overlap
            var hasConflictingAppointments = await _context.Appointments
                .Where(a => a.TechnicianId == technician.TechnicianId)
                .Where(a => a.StartTime < requestedEndTime && requestedStartTime < a.EndTime)
                .AnyAsync(cancellationToken);

            if (!hasConflictingAppointments)
            {
                candidateTechnicians.Add(technician);
            }
        }

        return candidateTechnicians;
    }

    /// <summary>
    /// Locks a service bay using PostgreSQL SELECT FOR UPDATE.
    /// This ensures exclusive access to the resource during the transaction.
    /// </summary>
    /// <param name="serviceBayId">The service bay to lock</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The locked service bay entity or null if locking failed</returns>
    private async Task<ServiceBay?> LockServiceBayAsync(int serviceBayId, CancellationToken cancellationToken)
    {
         var lockedServiceBay = await _context.ServiceBays
        .FromSqlRaw("SELECT * FROM \"ServiceBays\" WHERE \"ServiceBayId\" = {0} FOR UPDATE", serviceBayId)
        .FirstOrDefaultAsync(cancellationToken);

    return lockedServiceBay;
    }

    /// <summary>
    /// Locks a technician using PostgreSQL SELECT FOR UPDATE.
    /// This ensures exclusive access to the resource during the transaction.
    /// </summary>
    /// <param name="technicianId">The technician to lock</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The locked technician entity or null if locking failed</returns>
    private async Task<Technician?> LockTechnicianAsync(int technicianId, CancellationToken cancellationToken)
    {
        var lockedTechnician = await _context.Technicians
        .FromSqlRaw("SELECT * FROM \"Technicians\" WHERE \"TechnicianId\" = {0} FOR UPDATE", technicianId)
        .FirstOrDefaultAsync(cancellationToken);

    return lockedTechnician;
    }

    /// <summary>
    /// Rechecks service bay availability while the resource is locked.
    /// This is critical to ensure no other transaction booked the resource between initial check and locking.
    /// </summary>
    /// <param name="serviceBayId">The locked service bay to recheck</param>
    /// <param name="requestedStartTime">Start time of the requested interval</param>
    /// <param name="requestedEndTime">End time of the requested interval</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the service bay is still available, false otherwise</returns>
    private async Task<bool> IsServiceBayAvailableWhileLockedAsync(int serviceBayId, DateTime requestedStartTime, 
        DateTime requestedEndTime, CancellationToken cancellationToken)
    {
        // Check for any conflicting appointments using half-open interval overlap detection
        var hasConflictingAppointments = await _context.Appointments
            .Where(a => a.ServiceBayId == serviceBayId)
            .Where(a => a.StartTime < requestedEndTime && requestedStartTime < a.EndTime)
            .AnyAsync(cancellationToken);

        return !hasConflictingAppointments;
    }

    /// <summary>
    /// Rechecks technician availability while the resource is locked.
    /// This is critical to ensure no other transaction booked the resource between initial check and locking.
    /// </summary>
    /// <param name="technicianId">The locked technician to recheck</param>
    /// <param name="requestedStartTime">Start time of the requested interval</param>
    /// <param name="requestedEndTime">End time of the requested interval</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the technician is still available, false otherwise</returns>
    private async Task<bool> IsTechnicianAvailableWhileLockedAsync(int technicianId, DateTime requestedStartTime, 
        DateTime requestedEndTime, CancellationToken cancellationToken)
    {
        // Check for any conflicting appointments using half-open interval overlap detection
        var hasConflictingAppointments = await _context.Appointments
            .Where(a => a.TechnicianId == technicianId)
            .Where(a => a.StartTime < requestedEndTime && requestedStartTime < a.EndTime)
            .AnyAsync(cancellationToken);

        return !hasConflictingAppointments;
    }
}