using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class QueueServicePointConfiguration : IEntityTypeConfiguration<QueueServicePoint>
{
    public void Configure(EntityTypeBuilder<QueueServicePoint> builder)
    {
        builder.ToTable("queue_service_points");

        builder.HasKey(sp => sp.Id);
        builder.Property(sp => sp.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(sp => sp.HospitalId)
            .HasColumnName("hospital_id");

        builder.Property(sp => sp.RoomId)
            .HasColumnName("room_id");

        builder.Property(sp => sp.DoctorId)
            .HasColumnName("doctor_id");

        builder.Property(sp => sp.ServiceId)
            .HasColumnName("service_id");

        builder.Property(sp => sp.PointType)
            .HasColumnName("point_type")
            .HasMaxLength(20);

        builder.Property(sp => sp.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(sp => sp.IsActive)
            .HasColumnName("is_active");
    }
}
