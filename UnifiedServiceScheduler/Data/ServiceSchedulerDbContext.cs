using Microsoft.EntityFrameworkCore;

namespace UnifiedServiceScheduler.Data;

public class ServiceSchedulerDbContext : DbContext
{
    public ServiceSchedulerDbContext(DbContextOptions<ServiceSchedulerDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Model configurations will be added here as entities are created
    }
}