using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(a => a.AppointmentCode)
            .HasColumnName("appointment_code")
            .HasMaxLength(40);

        builder.Property(a => a.PatientId)
            .HasColumnName("patient_id");

        builder.Property(a => a.SlotId)
            .HasColumnName("slot_id");

        builder.Property(a => a.BookingChannel)
            .HasColumnName("booking_channel")
            .HasMaxLength(12);

        builder.Property(a => a.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(100);

        builder.Property(a => a.ReceptionNote)
            .HasColumnName("reception_note");

        builder.Property(a => a.ReasonForVisit)
            .HasColumnName("reason_for_visit");

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasMaxLength(20);

        builder.Property(a => a.BookedByUserId)
            .HasColumnName("booked_by_user_id");

        builder.Property(a => a.BookedAt)
            .HasColumnName("booked_at")
            .HasColumnType("timestamp without time zone");

        builder.HasOne(a => a.Slot)
            .WithMany(s => s.Appointments)
            .HasForeignKey(a => a.SlotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Patient)
            .WithMany()
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
