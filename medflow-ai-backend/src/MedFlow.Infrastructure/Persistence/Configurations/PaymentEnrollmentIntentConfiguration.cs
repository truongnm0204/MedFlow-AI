using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class PaymentEnrollmentIntentConfiguration : IEntityTypeConfiguration<PaymentEnrollmentIntent>
{
    public void Configure(EntityTypeBuilder<PaymentEnrollmentIntent> builder)
    {
        builder.ToTable("payment_enrollment_intents");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(x => x.PaymentId)
            .HasColumnName("payment_id")
            .IsRequired();

        builder.Property(x => x.VisitId)
            .HasColumnName("visit_id")
            .IsRequired();

        builder.Property(x => x.PatientId)
            .HasColumnName("patient_id")
            .IsRequired();

        builder.Property(x => x.DiagnosticOrderId)
            .HasColumnName("diagnostic_order_id")
            .IsRequired();

        builder.Property(x => x.RequestedByUserId)
            .HasColumnName("requested_by_user_id");

        builder.Property(x => x.RequestedByRole)
            .HasColumnName("requested_by_role")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.PresenceVerifiedDate)
            .HasColumnName("presence_verified_date")
            .HasColumnType("date");

        builder.Property(x => x.PresenceSessionNo)
            .HasColumnName("presence_session_no");

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.RetryCount)
            .HasColumnName("retry_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.OutcomeCode)
            .HasColumnName("outcome_code")
            .HasMaxLength(64);

        builder.Property(x => x.OutcomeMessage)
            .HasColumnName("outcome_message")
            .HasMaxLength(500);

        builder.Property(x => x.QueueEntryId)
            .HasColumnName("queue_entry_id");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        builder.Property(x => x.ProcessedAt)
            .HasColumnName("processed_at")
            .HasColumnType("timestamp without time zone");

        builder.HasIndex(x => x.PaymentId)
            .IsUnique()
            .HasDatabaseName("uq_payment_enrollment_intents_payment_id");

        builder.HasIndex(x => x.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("uq_payment_enrollment_intents_idempotency_key");

        builder.HasIndex(x => x.VisitId)
            .HasDatabaseName("ix_payment_enrollment_intents_visit_id");

        builder.HasIndex(x => x.PatientId)
            .HasDatabaseName("ix_payment_enrollment_intents_patient_id");

        builder.HasIndex(x => x.DiagnosticOrderId)
            .HasDatabaseName("ix_payment_enrollment_intents_diagnostic_order_id");

        builder.HasIndex(x => x.RequestedByUserId)
            .HasDatabaseName("ix_payment_enrollment_intents_requested_by_user_id");

        builder.HasIndex(x => x.QueueEntryId)
            .HasDatabaseName("ix_payment_enrollment_intents_queue_entry_id");

        builder.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("ix_payment_enrollment_intents_recovery")
            .HasFilter("\"status\" IN ('PENDING_SETTLEMENT', 'PENDING_POST_COMMIT')");

        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(x => x.VisitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DiagnosticOrder>()
            .WithMany()
            .HasForeignKey(x => x.DiagnosticOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<QueueEntry>()
            .WithMany()
            .HasForeignKey(x => x.QueueEntryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
