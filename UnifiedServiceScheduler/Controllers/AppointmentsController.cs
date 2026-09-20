using Microsoft.AspNetCore.Mvc;
using UnifiedServiceScheduler.Services;

namespace UnifiedServiceScheduler.Controllers;

/// <summary>
/// Controller for managing service appointments
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly AppointmentService _appointmentService;

    public AppointmentsController(AppointmentService appointmentService)
    {
        _appointmentService = appointmentService ?? throw new ArgumentNullException(nameof(appointmentService));
    }

    /// <summary>
    /// Creates a new service appointment
    /// </summary>
    /// <param name="request">Appointment creation request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created appointment details</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequest request, 
        CancellationToken cancellationToken = default)
    {
        // Validate request model
        if (request == null)
        {
            return BadRequest(new { Error = "Request body cannot be null" });
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Call the AppointmentService to handle business logic
        var result = await _appointmentService.CreateAppointmentAsync(request, cancellationToken);

        if (result.Success)
        {
            // Map successful creation -> 201 Created
          return CreatedAtAction(
            nameof(GetAppointment),
            new { appointmentId = result.CreatedAppointment!.AppointmentId },
                new
                {
                    AppointmentId = result.CreatedAppointment.AppointmentId,
                    CustomerId = result.CreatedAppointment.CustomerId,
                    VehicleVin = result.CreatedAppointment.VehicleVin,
                    ServiceTypeId = result.CreatedAppointment.ServiceTypeId,
                    DealershipId = result.CreatedAppointment.DealershipId,
                    StartTime = result.CreatedAppointment.StartTime,
                    EndTime = result.CreatedAppointment.EndTime,
                    ServiceBayId = result.CreatedAppointment.ServiceBayId,
                    TechnicianId = result.CreatedAppointment.TechnicianId,
                    CreatedAt = result.CreatedAppointment.CreatedAt
                });
        }

        // Map failure types to appropriate HTTP status codes
        return result.ErrorType switch
        {
            AppointmentCreationErrorType.ValidationFailure => BadRequest(new { Error = result.ErrorMessage }),
            AppointmentCreationErrorType.ServiceTypeNotFound => BadRequest(new { Error = result.ErrorMessage }),
            AppointmentCreationErrorType.ResourceConflict => Conflict(new { Error = result.ErrorMessage }),
            _ => BadRequest(new { Error = result.ErrorMessage })
        };
    }

    /// <summary>
    /// Retrieves an appointment by ID
    /// </summary>
    /// <param name="appointmentId">The appointment ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Appointment details if found</returns>
    [HttpGet("{appointmentId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAppointment(int appointmentId, CancellationToken cancellationToken = default)
    {
        var appointment = await _appointmentService.GetAppointmentByIdAsync(appointmentId, cancellationToken);

        if (appointment == null)
        {
            return NotFound(new { Error = $"Appointment with ID {appointmentId} not found" });
        }

        return Ok(new
        {
            AppointmentId = appointment.AppointmentId,
            CustomerId = appointment.CustomerId,
            VehicleVin = appointment.VehicleVin,
            ServiceTypeId = appointment.ServiceTypeId,
            DealershipId = appointment.DealershipId,
            StartTime = appointment.StartTime,
            EndTime = appointment.EndTime,
            ServiceBayId = appointment.ServiceBayId,
            TechnicianId = appointment.TechnicianId,
            CreatedAt = appointment.CreatedAt
        });
    }
}