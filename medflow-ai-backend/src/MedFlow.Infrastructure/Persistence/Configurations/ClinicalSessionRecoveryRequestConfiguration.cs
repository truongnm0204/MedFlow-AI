using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class ClinicalSessionRecoveryRequestConfiguration : IEntityTypeConfiguration<ClinicalSessionRecoveryRequest>
{
    public void Configure(EntityTypeBuilder<ClinicalSessionRecoveryRequest> builder)
    {
        builder.ToTable("clinical_session_recovery_requests", table =>
        {
            table.HasCheckConstraint(
                "chk_csrr_status",
                "\"status\" IN ('REQUESTED','GRANTED','REJECTED','CANCELLED','RECOVERED','REVOKED','EXPIRED')");

            table.HasCheckConstraint(
                "chk_csrr_queue_state_positive",
                "\"expected_queue_state_version\" >= 1");

            table.HasCheckConstraint(
                "chk_csrr_no_self_grant",
                "\"granted_by_doctor_id\" IS NULL OR \"authorized_doctor_id\" IS NULL OR \"granted_by_doctor_id\" <> \"authorized_doctor_id\"");

            table.HasCheckConstraint(
                "chk_csrr_idempotency_tuple",
                "(\"idempotency_key\" IS NULL AND \"idempotency_actor_user_id\" IS NULL AND \"idempotency_operation\" IS NULL) OR (\"idempotency_key\" IS NOT NULL AND \"idempotency_actor_user_id\" IS NOT NULL AND \"idempotency_operation\" IS NOT NULL)");

            table.HasCheckConstraint(
                "chk_csrr_status_metadata",
                "(\"status\" = 'REQUESTED' AND \"granted_by_user_id\" IS NULL AND \"granted_at\" IS NULL AND \"authorized_doctor_id\" IS NULL AND \"recovered_by_user_id\" IS NULL AND \"rejected_by_user_id\" IS NULL AND \"cancelled_by_user_id\" IS NULL AND \"revoked_by_user_id\" IS NULL AND \"expired_at\" IS NULL) OR " +
                "(\"status\" = 'GRANTED' AND \"authorized_doctor_id\" IS NOT NULL AND \"granted_by_user_id\" IS NOT NULL AND \"granted_by_doctor_id\" IS NOT NULL AND \"granted_at\" IS NOT NULL AND \"expires_at\" IS NOT NULL AND btrim(coalesce(\"grant_reason\", '')) <> '') OR " +
                "(\"status\" = 'RECOVERED' AND \"recovered_by_user_id\" IS NOT NULL AND \"recovered_by_doctor_id\" IS NOT NULL AND \"recovered_at\" IS NOT NULL AND btrim(coalesce(\"recovery_reason\", '')) <> '' AND (\"own_session\" OR \"authorized_doctor_id\" IS NOT NULL)) OR " +
                "(\"status\" = 'REJECTED' AND \"rejected_by_user_id\" IS NOT NULL AND \"rejected_at\" IS NOT NULL AND btrim(coalesce(\"rejection_reason\", '')) <> '') OR " +
                "(\"status\" = 'CANCELLED' AND \"cancelled_by_user_id\" IS NOT NULL AND \"cancelled_at\" IS NOT NULL AND btrim(coalesce(\"cancellation_reason\", '')) <> '') OR " +
                "(\"status\" = 'REVOKED' AND \"revoked_by_user_id\" IS NOT NULL AND \"revoked_at\" IS NOT NULL AND btrim(coalesce(\"revocation_reason\", '')) <> '') OR " +
                "(\"status\" = 'EXPIRED' AND \"expired_at\" IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(x => x.HospitalId).HasColumnName("hospital_id").IsRequired();
        builder.Property(x => x.VisitId).HasColumnName("visit_id").IsRequired();
        builder.Property(x => x.QueueEntryId).HasColumnName("queue_entry_id").IsRequired();
        builder.Property(x => x.OriginalDoctorId).HasColumnName("original_doctor_id");
        builder.Property(x => x.AuthorizedDoctorId).HasColumnName("authorized_doctor_id");

        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id").IsRequired();
        builder.Property(x => x.RequestedAt)
            .HasColumnName("requested_at")
            .HasColumnType("timestamp without time zone")
            .IsRequired();
        builder.Property(x => x.RequestReason).HasColumnName("request_reason").HasMaxLength(500).IsRequired();

        builder.Property(x => x.GrantedByUserId).HasColumnName("granted_by_user_id");
        builder.Property(x => x.GrantedByDoctorId).HasColumnName("granted_by_doctor_id");
        builder.Property(x => x.GrantedAt).HasColumnName("granted_at").HasColumnType("timestamp without time zone");
        builder.Property(x => x.GrantReason).HasColumnName("grant_reason").HasMaxLength(500);
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp without time zone");

        builder.Property(x => x.ExpectedQueueStateVersion).HasColumnName("expected_queue_state_version").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(x => x.OwnSession).HasColumnName("own_session").IsRequired();

        builder.Property(x => x.RecoveredByUserId).HasColumnName("recovered_by_user_id");
        builder.Property(x => x.RecoveredByDoctorId).HasColumnName("recovered_by_doctor_id");
        builder.Property(x => x.RecoveredAt).HasColumnName("recovered_at").HasColumnType("timestamp without time zone");
        builder.Property(x => x.RecoveryReason).HasColumnName("recovery_reason").HasMaxLength(500);

        builder.Property(x => x.RevokedByUserId).HasColumnName("revoked_by_user_id");
        builder.Property(x => x.RevokedByDoctorId).HasColumnName("revoked_by_doctor_id");
        builder.Property(x => x.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamp without time zone");
        builder.Property(x => x.RevocationReason).HasColumnName("revocation_reason").HasMaxLength(500);

        builder.Property(x => x.RejectedByUserId).HasColumnName("rejected_by_user_id");
        builder.Property(x => x.RejectedAt).HasColumnName("rejected_at").HasColumnType("timestamp without time zone");
        builder.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(500);

        builder.Property(x => x.CancelledByUserId).HasColumnName("cancelled_by_user_id");
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at").HasColumnType("timestamp without time zone");
        builder.Property(x => x.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(500);

        builder.Property(x => x.ExpiredAt).HasColumnName("expired_at").HasColumnType("timestamp without time zone");

        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
        builder.Property(x => x.IdempotencyActorUserId).HasColumnName("idempotency_actor_user_id");
        builder.Property(x => x.IdempotencyOperation).HasColumnName("idempotency_operation").HasMaxLength(16);
        builder.Property(x => x.ConfirmationPayloadHash).HasColumnName("confirmation_payload_hash").HasMaxLength(64);

        builder.Property(x => x.StateVersion).HasColumnName("state_version").IsConcurrencyToken().IsRequired();
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp without time zone")
            .IsRequired();
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        builder.HasIndex(x => x.VisitId).HasDatabaseName("ix_csrr_visit_id");
        builder.HasIndex(x => x.QueueEntryId).HasDatabaseName("ix_csrr_queue_entry_id");
        builder.HasIndex(x => x.HospitalId).HasDatabaseName("ix_csrr_hospital_id");
        builder.HasIndex(x => x.OriginalDoctorId).HasDatabaseName("ix_csrr_original_doctor_id");
        builder.HasIndex(x => x.AuthorizedDoctorId).HasDatabaseName("ix_csrr_authorized_doctor_id");
        builder.HasIndex(x => new { x.Status, x.ExpiresAt }).HasDatabaseName("ix_csrr_status_expires_at");
        builder.HasIndex(x => x.QueueEntryId)
            .IsUnique()
            .HasDatabaseName("ux_csrr_one_unresolved_per_queue_entry")
            .HasFilter("\"status\" IN ('REQUESTED','GRANTED')");
        builder.HasIndex(x => new { x.IdempotencyActorUserId, x.IdempotencyOperation, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_csrr_idempotency_actor_operation_key")
            .HasFilter("\"idempotency_key\" IS NOT NULL AND \"idempotency_actor_user_id\" IS NOT NULL AND \"idempotency_operation\" IS NOT NULL");

        builder.HasOne<Hospital>()
            .WithMany()
            .HasForeignKey(x => x.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);

        // Database migration 028 additionally enforces the composite FK (queue_entry_id, visit_id)
        // -> queue_entries (id, visit_id); EF maps the individual keys for query/composition use.
        builder.HasOne<QueueEntry>()
            .WithMany()
            .HasForeignKey(x => x.QueueEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(x => x.VisitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(x => x.OriginalDoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(x => x.AuthorizedDoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(x => x.GrantedByDoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(x => x.RecoveredByDoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(x => x.RevokedByDoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.GrantedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RecoveredByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RevokedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RejectedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CancelledByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.IdempotencyActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
