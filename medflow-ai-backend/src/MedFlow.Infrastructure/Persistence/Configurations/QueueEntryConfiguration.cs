using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class QueueEntryConfiguration : IEntityTypeConfiguration<QueueEntry>
{
    public void Configure(EntityTypeBuilder<QueueEntry> builder)
    {
        builder.ToTable("queue_entries");

        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(q => q.VisitId)
            .HasColumnName("visit_id");

        builder.Property(q => q.AppointmentId)
            .HasColumnName("appointment_id");

        builder.Property(q => q.ServicePointId)
            .HasColumnName("service_point_id");

        builder.Property(q => q.CurrentServicePointId)
            .HasColumnName("current_service_point_id");

        builder.Property(q => q.QueueDate)
            .HasColumnName("queue_date");

        builder.Property(q => q.QueueNumber)
            .HasColumnName("queue_number");

        builder.Property(q => q.QueueType)
            .HasColumnName("queue_type")
            .HasMaxLength(20);

        builder.Property(q => q.Status)
            .HasColumnName("status")
            .HasMaxLength(20);

        builder.Property(q => q.ScheduledAt)
            .HasColumnName("scheduled_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(q => q.PresenceStatus)
            .HasColumnName("presence_status")
            .HasMaxLength(16);

        builder.Property(q => q.ArrivedAt)
            .HasColumnName("arrived_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(q => q.ArrivalClass)
            .HasColumnName("arrival_class")
            .HasMaxLength(20);

        builder.Property(q => q.ManualPriority)
            .HasColumnName("manual_priority")
            .HasMaxLength(10);

        builder.Property(q => q.StateVersion)
            .HasColumnName("state_version")
            .IsConcurrencyToken();

        builder.Property(q => q.ReadyAt)
            .HasColumnName("ready_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(q => q.CalledAt)
            .HasColumnName("called_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(q => q.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(q => q.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(q => q.BlockedReason)
            .HasColumnName("blocked_reason");

        builder.Property(q => q.BackfilledAt)
            .HasColumnName("backfilled_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(q => q.BackfillReason)
            .HasColumnName("backfill_reason");

        builder.Property(q => q.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(100);

        builder.Property(q => q.CoordinationPurpose)
            .HasColumnName("coordination_purpose")
            .HasMaxLength(24);

        builder.Property(q => q.DiagnosticOrderId)
            .HasColumnName("diagnostic_order_id");

        builder.Property(q => q.PrescriptionId)
            .HasColumnName("prescription_id");

        builder.HasIndex(q => q.DiagnosticOrderId)
            .HasDatabaseName("ix_queue_entries_diagnostic_order_id");

        builder.HasIndex(q => q.PrescriptionId)
            .HasDatabaseName("ix_queue_entries_prescription_id");

        builder.HasIndex(q => q.VisitId)
            .IsUnique()
            .HasDatabaseName("ux_queue_entries_one_active_post_exam_per_visit")
            .HasFilter("\"visit_id\" IS NOT NULL AND upper(btrim(coalesce(\"queue_type\", ''))) IN ('DIAGNOSTIC', 'RETURN_CONSULTATION', 'PHARMACY') AND upper(btrim(coalesce(\"status\", ''))) IN ('WAITING', 'READY', 'CALLED', 'IN_PROGRESS', 'IN_CONSULTATION', 'SERVING')");

        builder.HasOne<DiagnosticOrder>()
            .WithMany()
            .HasForeignKey(q => q.DiagnosticOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Prescription>()
            .WithMany()
            .HasForeignKey(q => q.PrescriptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
