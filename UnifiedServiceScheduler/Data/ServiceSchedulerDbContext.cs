using Microsoft.EntityFrameworkCore;
using UnifiedServiceScheduler.Data.Entities;

namespace UnifiedServiceScheduler.Data;

public class ServiceSchedulerDbContext : DbContext
{
    public ServiceSchedulerDbContext(DbContextOptions<ServiceSchedulerDbContext> options) : base(options)
    {
    }

    // DbSet properties
    public DbSet<Appointment> Appointments { get; set; }
    public DbSet<ServiceBay> ServiceBays { get; set; }
    public DbSet<Technician> Technicians { get; set; }
    public DbSet<ServiceType> ServiceTypes { get; set; }
    public DbSet<TechnicianQualification> TechnicianQualifications { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Configure entities and relationships
        ConfigureEntities(modelBuilder);
        
        // Seed data
        SeedData(modelBuilder);
    }

    private void ConfigureEntities(ModelBuilder modelBuilder)
    {
        // ServiceBay configuration
        modelBuilder.Entity<ServiceBay>(entity =>
        {
            entity.HasKey(e => e.ServiceBayId);
            entity.Property(e => e.BayNumber).HasMaxLength(50).IsRequired();
            entity.HasIndex(e => new { e.DealershipId, e.BayNumber })
                  .HasDatabaseName("IX_ServiceBay_Dealership_BayNumber_Unique")
                  .IsUnique();
        });

        // ServiceType configuration
        modelBuilder.Entity<ServiceType>(entity =>
        {
            entity.HasKey(e => e.ServiceTypeId);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(e => e.Name)
                  .HasDatabaseName("IX_ServiceType_Name_Unique")
                  .IsUnique();
        });

        // Technician configuration
        modelBuilder.Entity<Technician>(entity =>
        {
            entity.HasKey(e => e.TechnicianId);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(e => e.DealershipId)
                  .HasDatabaseName("IX_Technician_DealershipId");
        });

        // TechnicianQualification configuration (composite key)
        modelBuilder.Entity<TechnicianQualification>(entity =>
        {
            entity.HasKey(e => new { e.TechnicianId, e.ServiceTypeId });
            
            entity.HasOne(e => e.Technician)
                  .WithMany(t => t.TechnicianQualifications)
                  .HasForeignKey(e => e.TechnicianId)
                  .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasOne(e => e.ServiceType)
                  .WithMany(st => st.TechnicianQualifications)
                  .HasForeignKey(e => e.ServiceTypeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Appointment configuration
        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.HasKey(e => e.AppointmentId);
            entity.Property(e => e.VehicleVin).HasMaxLength(17).IsRequired();
            
            // Foreign key relationships
            entity.HasOne(e => e.ServiceType)
                  .WithMany(st => st.Appointments)
                  .HasForeignKey(e => e.ServiceTypeId)
                  .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(e => e.ServiceBay)
                  .WithMany(sb => sb.Appointments)
                  .HasForeignKey(e => e.ServiceBayId)
                  .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(e => e.Technician)
                  .WithMany(t => t.Appointments)
                  .HasForeignKey(e => e.TechnicianId)
                  .OnDelete(DeleteBehavior.Restrict);
            
            // Indexes for scheduling queries
            entity.HasIndex(e => new { e.ServiceBayId, e.StartTime, e.EndTime })
                  .HasDatabaseName("IX_Appointment_ServiceBay_TimeSlot");
            
            entity.HasIndex(e => new { e.TechnicianId, e.StartTime, e.EndTime })
                  .HasDatabaseName("IX_Appointment_Technician_TimeSlot");
        });
    }

    private void SeedData(ModelBuilder modelBuilder)
    {
        // Seed ServiceBays
        modelBuilder.Entity<ServiceBay>().HasData(
            new ServiceBay { ServiceBayId = 1, DealershipId = 1, BayNumber = "BAY-001", IsActive = true },
            new ServiceBay { ServiceBayId = 2, DealershipId = 1, BayNumber = "BAY-002", IsActive = true },
            new ServiceBay { ServiceBayId = 3, DealershipId = 1, BayNumber = "BAY-003", IsActive = true }
        );

        // Seed ServiceTypes
        modelBuilder.Entity<ServiceType>().HasData(
            new ServiceType { ServiceTypeId = 1, Name = "Oil Change", Duration = TimeSpan.FromMinutes(30) },
            new ServiceType { ServiceTypeId = 2, Name = "Brake Repair", Duration = TimeSpan.FromHours(2) },
            new ServiceType { ServiceTypeId = 3, Name = "Tire Rotation", Duration = TimeSpan.FromMinutes(45) },
            new ServiceType { ServiceTypeId = 4, Name = "Engine Diagnostic", Duration = TimeSpan.FromHours(1) },
            new ServiceType { ServiceTypeId = 5, Name = "Transmission Service", Duration = TimeSpan.FromMinutes(90) }
        );

        // Seed Technicians
        modelBuilder.Entity<Technician>().HasData(
            new Technician { TechnicianId = 1, DealershipId = 1, Name = "John Smith", IsActive = true },
            new Technician { TechnicianId = 2, DealershipId = 1, Name = "Sarah Johnson", IsActive = true },
            new Technician { TechnicianId = 3, DealershipId = 1, Name = "Mike Wilson", IsActive = true }
        );

        // Seed TechnicianQualifications
        modelBuilder.Entity<TechnicianQualification>().HasData(
            new TechnicianQualification { TechnicianId = 1, ServiceTypeId = 1 }, // John - Oil Change
            new TechnicianQualification { TechnicianId = 1, ServiceTypeId = 3 }, // John - Tire Rotation
            new TechnicianQualification { TechnicianId = 2, ServiceTypeId = 2 }, // Sarah - Brake Repair
            new TechnicianQualification { TechnicianId = 2, ServiceTypeId = 4 }, // Sarah - Engine Diagnostic
            new TechnicianQualification { TechnicianId = 3, ServiceTypeId = 1 }, // Mike - Oil Change
            new TechnicianQualification { TechnicianId = 3, ServiceTypeId = 2 }, // Mike - Brake Repair
            new TechnicianQualification { TechnicianId = 3, ServiceTypeId = 3 }, // Mike - Tire Rotation
            new TechnicianQualification { TechnicianId = 3, ServiceTypeId = 4 }, // Mike - Engine Diagnostic
            new TechnicianQualification { TechnicianId = 3, ServiceTypeId = 5 }  // Mike - Transmission Service
        );
    }
}