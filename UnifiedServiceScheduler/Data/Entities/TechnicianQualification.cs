namespace UnifiedServiceScheduler.Data.Entities;

public class TechnicianQualification
{
    public int TechnicianId { get; set; }
    
    public int ServiceTypeId { get; set; }
    
    // Navigation properties
    public virtual Technician Technician { get; set; } = null!;
    public virtual ServiceType ServiceType { get; set; } = null!;
}