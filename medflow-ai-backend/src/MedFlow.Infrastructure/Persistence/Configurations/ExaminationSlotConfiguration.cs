using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class ExaminationSlotConfiguration : IEntityTypeConfiguration<ExaminationSlot>
{
    public void Configure(EntityTypeBuilder<ExaminationSlot> builder)
    {
        builder.ToTable("examination_slots");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(s => s.ServiceId)
            .HasColumnName("service_id");

        builder.Property(s => s.DoctorId)
            .HasColumnName("doctor_id");

        builder.Property(s => s.RoomId)
            .HasColumnName("room_id");

        builder.Property(s => s.StartsAt)
            .HasColumnName("starts_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(s => s.EndsAt)
            .HasColumnName("ends_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(s => s.Capacity)
            .HasColumnName("capacity");

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasMaxLength(16);

        builder.HasOne(s => s.Service)
            .WithMany()
            .HasForeignKey(s => s.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Doctor)
            .WithMany(d => d.ExaminationSlots)
            .HasForeignKey(s => s.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Room)
            .WithMany()
            .HasForeignKey(s => s.RoomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.StartsAt, s.EndsAt })
            .HasDatabaseName("ix_examination_slots_time_window");

        builder.HasIndex(s => new { s.DoctorId, s.StartsAt })
            .IsUnique()
            .HasDatabaseName("uq_examination_slots_active_doctor_start")
            .HasFilter("\"doctor_id\" IS NOT NULL AND \"status\" <> 'CLOSED'");

        builder.HasIndex(s => new { s.RoomId, s.StartsAt })
            .IsUnique()
            .HasDatabaseName("uq_examination_slots_active_room_start")
            .HasFilter("\"room_id\" IS NOT NULL AND \"status\" <> 'CLOSED'");

        builder.HasIndex(s => new { s.DoctorId, s.ServiceId, s.StartsAt })
            .IsUnique()
            .HasDatabaseName("uq_examination_slots_doctor_service_start")
            .HasFilter("\"doctor_id\" IS NOT NULL AND \"service_id\" IS NOT NULL");

        builder.HasIndex(s => new { s.ServiceId, s.RoomId, s.StartsAt })
            .IsUnique()
            .HasDatabaseName("uq_examination_slots_unassigned_service_room_start")
            .HasFilter("\"doctor_id\" IS NULL AND \"service_id\" IS NOT NULL AND \"room_id\" IS NOT NULL");
    }
}
