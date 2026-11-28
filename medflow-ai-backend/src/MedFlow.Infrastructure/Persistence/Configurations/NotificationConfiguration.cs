using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(n => n.PatientId)
            .HasColumnName("patient_id");

        builder.Property(n => n.RecipientUserId)
            .HasColumnName("recipient_user_id");

        builder.Property(n => n.AppointmentId)
            .HasColumnName("appointment_id");

        builder.Property(n => n.VisitId)
            .HasColumnName("visit_id");

        builder.Property(n => n.Channel)
            .HasColumnName("channel")
            .HasMaxLength(16);

        builder.Property(n => n.Recipient)
            .HasColumnName("recipient")
            .HasMaxLength(255);

        builder.Property(n => n.Subject)
            .HasColumnName("subject")
            .HasMaxLength(255);

        builder.Property(n => n.Body)
            .HasColumnName("body");

        builder.Property(n => n.Status)
            .HasColumnName("status")
            .HasMaxLength(16);

        builder.Property(n => n.RetryCount)
            .HasColumnName("retry_count");

        builder.Property(n => n.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(150);

        builder.Property(n => n.NotificationType)
            .HasColumnName("notification_type")
            .HasMaxLength(40);

        builder.Property(n => n.ProcessingToken)
            .HasColumnName("processing_token")
            .HasMaxLength(64);

        builder.Property(n => n.LeaseUntil)
            .HasColumnName("lease_until")
            .HasColumnType("timestamp without time zone");

        builder.Property(n => n.ScheduledAt)
            .HasColumnName("scheduled_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(n => n.SentAt)
            .HasColumnName("sent_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(n => n.ReadAt)
            .HasColumnName("read_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(n => n.LastError)
            .HasColumnName("last_error");

        builder.Property(n => n.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp without time zone");

        builder.HasIndex(n => n.PatientId)
            .HasDatabaseName("ix_notifications_patient_id");

        builder.HasIndex(n => n.RecipientUserId)
            .HasDatabaseName("ix_notifications_recipient_user_id")
            .HasFilter("\"recipient_user_id\" IS NOT NULL");

        builder.HasIndex(n => n.AppointmentId)
            .HasDatabaseName("ix_notifications_appointment_id");

        builder.HasIndex(n => n.VisitId)
            .HasDatabaseName("ix_notifications_visit_id");

        builder.HasIndex(n => n.ScheduledAt)
            .HasDatabaseName("ix_notifications_pending_due")
            .HasFilter("\"status\" = 'PENDING'");

        builder.HasIndex(n => n.LeaseUntil)
            .HasDatabaseName("ix_notifications_processing_lease")
            .HasFilter("\"status\" = 'PROCESSING'");

        builder.HasIndex(n => n.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("uq_notifications_idempotency_key")
            .HasFilter("\"idempotency_key\" IS NOT NULL");

        builder.HasIndex(n => new { n.PatientId, n.CreatedAt })
            .HasDatabaseName("ix_notifications_patient_unread")
            .IsDescending(false, true)
            .HasFilter("\"read_at\" IS NULL");

        builder.HasOne(n => n.Patient)
            .WithMany()
            .HasForeignKey(n => n.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(n => n.RecipientUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(n => n.Appointment)
            .WithMany()
            .HasForeignKey(n => n.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(n => n.Visit)
            .WithMany()
            .HasForeignKey(n => n.VisitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
