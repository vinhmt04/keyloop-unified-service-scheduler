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
                nameof(CreateAppointment), 
                new { id = result.CreatedAppointment!.AppointmentId }, 
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
}