using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class PatientFamilyLinkConfiguration : IEntityTypeConfiguration<PatientFamilyLink>
{
    public void Configure(EntityTypeBuilder<PatientFamilyLink> builder)
    {
        builder.ToTable("patient_family_links");

        builder.HasKey(l => new { l.OwnerPatientId, l.MemberPatientId });

        builder.Property(l => l.OwnerPatientId)
            .HasColumnName("owner_patient_id");

        builder.Property(l => l.MemberPatientId)
            .HasColumnName("member_patient_id");

        builder.Property(l => l.Relationship)
            .HasColumnName("relationship")
            .HasMaxLength(40);

        builder.Property(l => l.VerificationStatus)
            .HasColumnName("verification_status")
            .HasMaxLength(16);

        builder.Property(l => l.VerifiedAt)
            .HasColumnName("verified_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(l => l.ValidUntil)
            .HasColumnName("valid_until")
            .HasColumnType("timestamp without time zone");

        builder.Property(l => l.RevokedAt)
            .HasColumnName("revoked_at")
            .HasColumnType("timestamp without time zone");

        builder.HasOne(l => l.OwnerPatient)
            .WithMany()
            .HasForeignKey(l => l.OwnerPatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.MemberPatient)
            .WithMany()
            .HasForeignKey(l => l.MemberPatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
