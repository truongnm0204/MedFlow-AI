using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class PatientIdentityClaimConfiguration : IEntityTypeConfiguration<PatientIdentityClaim>
{
    public void Configure(EntityTypeBuilder<PatientIdentityClaim> builder)
    {
        builder.ToTable("patient_identity_claims");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(c => c.RequesterUserId).HasColumnName("requester_user_id");
        builder.Property(c => c.CurrentPatientId).HasColumnName("current_patient_id");
        builder.Property(c => c.TargetPatientId).HasColumnName("target_patient_id");
        builder.Property(c => c.RequestedPatientCode)
            .HasColumnName("requested_patient_code")
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(c => c.HospitalId).HasColumnName("hospital_id");
        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasMaxLength(24)
            .IsRequired();
        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(c => c.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(c => c.DecidedAt)
            .HasColumnName("decided_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(c => c.DecidedByStaffId).HasColumnName("decided_by_staff_id");
        builder.Property(c => c.DecisionNote).HasColumnName("decision_note").HasMaxLength(500);

        builder.HasIndex(c => c.RequesterUserId)
            .IsUnique()
            .HasFilter("status IN ('PENDING', 'NEEDS_RECONCILIATION')")
            .HasDatabaseName("uq_identity_claims_open_user");

        builder.HasIndex(c => new { c.TargetPatientId, c.Status })
            .HasDatabaseName("ix_identity_claims_target_status");
        builder.HasIndex(c => new { c.Status, c.ExpiresAt })
            .HasDatabaseName("ix_identity_claims_status_expires");
        builder.HasIndex(c => new { c.HospitalId, c.Status })
            .HasDatabaseName("ix_identity_claims_hospital_status");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.RequesterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(c => c.CurrentPatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(c => c.TargetPatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Hospital>()
            .WithMany()
            .HasForeignKey(c => c.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(c => c.DecidedByStaffId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
