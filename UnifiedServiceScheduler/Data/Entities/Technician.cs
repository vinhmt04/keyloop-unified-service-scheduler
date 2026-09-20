using System.ComponentModel.DataAnnotations;

namespace UnifiedServiceScheduler.Data.Entities;

public class Technician
{
    public int TechnicianId { get; set; }
    
    public int DealershipId { get; set; }
    
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    public bool IsActive { get; set; }
    
    // Navigation properties
    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public virtual ICollection<TechnicianQualification> TechnicianQualifications { get; set; } = new List<TechnicianQualification>();
}