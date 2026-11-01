using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class DoctorServiceAuthorizationConfiguration : IEntityTypeConfiguration<DoctorServiceAuthorization>
{
    public void Configure(EntityTypeBuilder<DoctorServiceAuthorization> builder)
    {
        builder.ToTable("doctor_service_authorizations");

        builder.HasKey(a => new { a.DoctorId, a.ServiceId });

        builder.Property(a => a.DoctorId)
            .HasColumnName("doctor_id");

        builder.Property(a => a.ServiceId)
            .HasColumnName("service_id");

        builder.Property(a => a.IsActive)
            .HasColumnName("is_active");

        builder.HasOne(a => a.Doctor)
            .WithMany(d => d.ServiceAuthorizations)
            .HasForeignKey(a => a.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Service)
            .WithMany()
            .HasForeignKey(a => a.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
