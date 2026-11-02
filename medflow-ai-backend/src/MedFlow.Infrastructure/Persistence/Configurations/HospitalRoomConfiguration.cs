using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class HospitalRoomConfiguration : IEntityTypeConfiguration<HospitalRoom>
{
    public void Configure(EntityTypeBuilder<HospitalRoom> builder)
    {
        builder.ToTable("hospital_rooms");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(r => r.HospitalId)
            .HasColumnName("hospital_id");

        builder.Property(r => r.DepartmentId)
            .HasColumnName("department_id");

        builder.Property(r => r.RoomCode)
            .HasColumnName("room_code")
            .HasMaxLength(40);

        builder.Property(r => r.Name)
            .HasColumnName("name")
            .HasMaxLength(200);

        builder.Property(r => r.RoomType)
            .HasColumnName("room_type")
            .HasMaxLength(20);

        builder.Property(r => r.Location)
            .HasColumnName("location");

        builder.Property(r => r.Capacity)
            .HasColumnName("capacity");

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasMaxLength(20);

        builder.HasIndex(r => new { r.HospitalId, r.RoomCode })
            .IsUnique()
            .HasDatabaseName("uq_hospital_rooms_hospital_id_room_code");

        builder.HasOne(r => r.Department)
            .WithMany(d => d.HospitalRooms)
            .HasForeignKey(r => r.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
