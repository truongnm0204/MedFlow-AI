using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class EkycVerificationSessionConfiguration : IEntityTypeConfiguration<EkycVerificationSession>
{
    public void Configure(EntityTypeBuilder<EkycVerificationSession> builder)
    {
        builder.ToTable("ekyc_verification_sessions", table =>
        {
            table.HasCheckConstraint(
                "chk_ekyc_status",
                "\"status\" IN ('INITIATED','DOCUMENT_FRONT_DONE','DOCUMENT_BACK_DONE','FACE_DONE','FINALIZED','VERIFIED','REJECTED','EXPIRED','CONSUMED')");

            table.HasCheckConstraint(
                "chk_ekyc_verification_method",
                "\"verification_method\" IN ('EKYC_VNPT','RECEPTION_MANUAL')");

            table.HasCheckConstraint(
                "chk_ekyc_no_self_verify",
                "\"verified_by\" IS NULL OR \"user_id\" IS NULL OR \"verified_by\" <> \"user_id\"");

            table.HasCheckConstraint(
                "chk_ekyc_attempt_count_nonnegative",
                "\"attempt_count\" >= 0");

            table.HasCheckConstraint(
                "chk_ekyc_method_verifier_consistency",
                "(\"verification_method\" = 'RECEPTION_MANUAL' AND \"verified_by\" IS NOT NULL) OR (\"verification_method\" = 'EKYC_VNPT' AND \"verified_by\" IS NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.RegistrationSessionId).HasColumnName("registration_session_id");
        builder.Property(x => x.PatientId).HasColumnName("patient_id");

        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.VerificationMethod).HasColumnName("verification_method").HasMaxLength(32).IsRequired();

        builder.Property(x => x.OcrSnapshot).HasColumnName("ocr_snapshot").HasColumnType("jsonb");
        builder.Property(x => x.LivenessVerdicts).HasColumnName("liveness_verdicts").HasColumnType("jsonb");
        builder.Property(x => x.CompareResult).HasColumnName("compare_result").HasColumnType("jsonb");
        builder.Property(x => x.TamperingVerdict).HasColumnName("tampering_verdict").HasColumnType("jsonb");

        builder.Property(x => x.EvidenceDataSign).HasColumnName("evidence_datasign");
        builder.Property(x => x.EvidencePayloadDigest).HasColumnName("evidence_payload_digest");
        builder.Property(x => x.EvidencePayloadFull).HasColumnName("evidence_payload_full");

        builder.Property(x => x.IdentificationHmac).HasColumnName("identification_hmac").HasMaxLength(128);

        builder.Property(x => x.ConsentVersion).HasColumnName("consent_version").HasMaxLength(32);
        builder.Property(x => x.ConsentAcceptedAt)
            .HasColumnName("consent_accepted_at")
            .HasColumnType("timestamp without time zone");
        builder.Property(x => x.ClientIp).HasColumnName("client_ip").HasColumnType("inet");
        builder.Property(x => x.UserAgent).HasColumnName("user_agent");

        builder.Property(x => x.VerifiedBy).HasColumnName("verified_by");
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason");
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();

        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp without time zone")
            .IsRequired();
        builder.Property(x => x.ConsumedAt)
            .HasColumnName("consumed_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp without time zone")
            .IsRequired();
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        builder.HasIndex(x => x.IdentificationHmac).HasDatabaseName("ix_ekyc_sessions_identification_hmac");
        builder.HasIndex(x => x.UserId).HasDatabaseName("ix_ekyc_sessions_user_id");
        builder.HasIndex(x => x.PatientId).HasDatabaseName("ix_ekyc_sessions_patient_id");
        builder.HasIndex(x => new { x.Status, x.ExpiresAt }).HasDatabaseName("ix_ekyc_sessions_status_expires_at");
    }
}
