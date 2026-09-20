using Microsoft.EntityFrameworkCore;
using System.Data;
using UnifiedServiceScheduler.Data;
using UnifiedServiceScheduler.Data.Entities;

namespace UnifiedServiceScheduler.Services;

/// <summary>
/// Request model for creating a new appointment
/// </summary>
public class CreateAppointmentRequest
{
    public int CustomerId { get; set; }
    public string VehicleVin { get; set; } = string.Empty;
    public int ServiceTypeId { get; set; }
    public int DealershipId { get; set; }
    public DateTime StartTime { get; set; }
}

/// <summary>
/// Result of appointment creation operation
/// </summary>
public class CreateAppointmentResult
{
    public bool Success { get; set; }
    public Appointment? CreatedAppointment { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Service responsible for creating appointments with proper resource assignment and transactional integrity.
/// Implements Task 4.3: PostgreSQL READ COMMITTED transactions with resource locking through SaveChanges.
/// </summary>
public class AppointmentService
{
    private readonly ServiceSchedulerDbContext _context;
    private readonly ResourceAssignmentService _resourceAssignmentService;

    public AppointmentService(ServiceSchedulerDbContext context, ResourceAssignmentService resourceAssignmentService)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _resourceAssignmentService = resourceAssignmentService ?? throw new ArgumentNullException(nameof(resourceAssignmentService));
    }

    /// <summary>
    /// Creates a new appointment with proper resource assignment and transactional integrity.
    /// Uses PostgreSQL READ COMMITTED isolation level with resource locking held through commit.
    /// </summary>
    /// <param name="request">Appointment creation request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure with details</returns>
    public async Task<CreateAppointmentResult> CreateAppointmentAsync(CreateAppointmentRequest request, 
        CancellationToken cancellationToken = default)
    {
        // Validate input
        if (request == null)
        {
            return new CreateAppointmentResult
            {
                Success = false,
                ErrorMessage = "Request cannot be null"
            };
        }

        if (string.IsNullOrWhiteSpace(request.VehicleVin) || request.VehicleVin.Length > 17)
        {
            return new CreateAppointmentResult
            {
                Success = false,
                ErrorMessage = "Vehicle VIN must be provided and cannot exceed 17 characters"
            };
        }

        // Begin PostgreSQL READ COMMITTED transaction
        using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        
        // Step 1: Calculate appointment end time from service type duration
        var serviceType = await _context.ServiceTypes
            .FirstOrDefaultAsync(st => st.ServiceTypeId == request.ServiceTypeId, cancellationToken);
        
        if (serviceType == null)
        {
            return new CreateAppointmentResult
            {
                Success = false,
                ErrorMessage = $"Service type with ID {request.ServiceTypeId} not found"
            };
        }

        var endTime = request.StartTime.Add(serviceType.Duration);

        // Step 2: Call ResourceAssignmentService within this transaction
        // The ResourceAssignmentService will handle resource locking using SELECT FOR UPDATE
        var resourceAssignmentResult = await _resourceAssignmentService.AssignResourcesAsync(
            request.DealershipId, 
            request.ServiceTypeId, 
            request.StartTime, 
            endTime, 
            cancellationToken);

        if (!resourceAssignmentResult.Success)
        {
            return new CreateAppointmentResult
            {
                Success = false,
                ErrorMessage = $"Resource assignment failed: {resourceAssignmentResult.ErrorMessage}"
            };
        }

        // Step 3: Create the Appointment entity only after resources are successfully assigned
        var appointment = new Appointment
        {
            CustomerId = request.CustomerId,
            VehicleVin = request.VehicleVin,
            ServiceTypeId = request.ServiceTypeId,
            DealershipId = request.DealershipId,
            StartTime = request.StartTime,
            EndTime = endTime,
            ServiceBayId = resourceAssignmentResult.AssignedServiceBay!.ServiceBayId,
            TechnicianId = resourceAssignmentResult.AssignedTechnician!.TechnicianId,
            CreatedAt = DateTime.UtcNow
        };

        // Add the appointment to the context
        _context.Appointments.Add(appointment);

        // Step 4: SaveChanges while keeping resource locks held through commit
        // The SELECT FOR UPDATE locks from ResourceAssignmentService remain active until commit
        await _context.SaveChangesAsync(cancellationToken);

        // Step 5: Commit the transaction (releases all locks atomically)
        await transaction.CommitAsync(cancellationToken);

        return new CreateAppointmentResult
        {
            Success = true,
            CreatedAppointment = appointment
        };
    }
}