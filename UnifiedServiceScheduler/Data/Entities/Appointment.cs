using System.ComponentModel.DataAnnotations;

namespace UnifiedServiceScheduler.Data.Entities;

public class Appointment
{
    public int AppointmentId { get; set; }
    
    public int CustomerId { get; set; }
    
    [MaxLength(17)]
    public string VehicleVin { get; set; } = string.Empty;
    
    public int ServiceTypeId { get; set; }
    
    public int DealershipId { get; set; }
    
    public DateTime StartTime { get; set; }
    
    public DateTime EndTime { get; set; }
    
    public int ServiceBayId { get; set; }
    
    public int TechnicianId { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public virtual ServiceType ServiceType { get; set; } = null!;
    public virtual ServiceBay ServiceBay { get; set; } = null!;
    public virtual Technician Technician { get; set; } = null!;
}