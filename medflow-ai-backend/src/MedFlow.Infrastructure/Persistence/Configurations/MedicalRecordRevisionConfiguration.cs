using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class MedicalRecordRevisionConfiguration : IEntityTypeConfiguration<MedicalRecordRevision>
{
    public void Configure(EntityTypeBuilder<MedicalRecordRevision> builder)
    {
        builder.ToTable("medical_record_revisions");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(r => r.MedicalRecordId)
            .HasColumnName("medical_record_id");

        builder.Property(r => r.RevisionNo)
            .HasColumnName("revision_no");

        builder.Property(r => r.PresentingComplaint)
            .HasColumnName("presenting_complaint");

        builder.Property(r => r.SymptomHistory)
            .HasColumnName("symptom_history");

        builder.Property(r => r.RelevantHistoryAllergies)
            .HasColumnName("relevant_history_allergies");

        builder.Property(r => r.VitalSigns)
            .HasColumnName("vital_signs")
            .HasColumnType("jsonb");

        builder.Property(r => r.ClinicalFindings)
            .HasColumnName("clinical_findings");

        builder.Property(r => r.ClinicalConclusion)
            .HasColumnName("clinical_conclusion");

        builder.Property(r => r.EditedByDoctorId)
            .HasColumnName("edited_by_doctor_id");

        builder.Property(r => r.RecordedAt)
            .HasColumnName("recorded_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(r => r.IsConfirmed)
            .HasColumnName("is_confirmed");

        builder.Property(r => r.AmendmentReason)
            .HasColumnName("amendment_reason");
    }
}
