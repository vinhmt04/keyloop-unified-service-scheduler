using System.ComponentModel.DataAnnotations;

namespace UnifiedServiceScheduler.Data.Entities;

public class ServiceBay
{
    public int ServiceBayId { get; set; }
    
    public int DealershipId { get; set; }
    
    [MaxLength(50)]
    public string BayNumber { get; set; } = string.Empty;
    
    public bool IsActive { get; set; }
    
    // Navigation properties
    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}