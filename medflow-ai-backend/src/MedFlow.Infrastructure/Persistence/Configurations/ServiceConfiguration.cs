using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(s => s.HospitalId)
            .HasColumnName("hospital_id");

        builder.Property(s => s.DepartmentId)
            .HasColumnName("department_id");

        builder.Property(s => s.SpecialtyId)
            .HasColumnName("specialty_id");

        builder.Property(s => s.ServiceCode)
            .HasColumnName("service_code")
            .HasMaxLength(40);

        builder.Property(s => s.Name)
            .HasColumnName("name")
            .HasMaxLength(200);

        builder.Property(s => s.ServiceType)
            .HasColumnName("service_type")
            .HasMaxLength(20);

        builder.Property(s => s.Description)
            .HasColumnName("description");

        builder.Property(s => s.AllowsOnlineBooking)
            .HasColumnName("allows_online_booking");

        builder.Property(s => s.RequiresResultVerification)
            .HasColumnName("requires_result_verification");

        builder.Property(s => s.IsActive)
            .HasColumnName("is_active");

        builder.HasIndex(s => new { s.HospitalId, s.ServiceCode })
            .IsUnique()
            .HasDatabaseName("uq_services_hospital_id_service_code");

        builder.HasOne(s => s.Department)
            .WithMany(d => d.Services)
            .HasForeignKey(s => s.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Specialty)
            .WithMany(sp => sp.Services)
            .HasForeignKey(s => s.SpecialtyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.ServicePrices)
            .WithOne(p => p.Service)
            .HasForeignKey(p => p.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
