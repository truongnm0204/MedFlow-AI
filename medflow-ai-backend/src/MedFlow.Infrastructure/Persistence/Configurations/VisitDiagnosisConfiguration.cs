using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class VisitDiagnosisConfiguration : IEntityTypeConfiguration<VisitDiagnosis>
{
    public void Configure(EntityTypeBuilder<VisitDiagnosis> builder)
    {
        builder.ToTable("visit_diagnoses");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(d => d.VisitId)
            .HasColumnName("visit_id");

        builder.Property(d => d.Icd10CodeId)
            .HasColumnName("icd10_code_id");

        builder.Property(d => d.IsPrimary)
            .HasColumnName("is_primary");

        builder.Property(d => d.CodeSnapshot)
            .HasColumnName("code_snapshot")
            .HasMaxLength(20);

        builder.Property(d => d.NameSnapshot)
            .HasColumnName("name_snapshot");

        builder.Property(d => d.CatalogVersion)
            .HasColumnName("catalog_version")
            .HasMaxLength(30);

        builder.Property(d => d.ClinicalDiagnosis)
            .HasColumnName("clinical_diagnosis");

        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasMaxLength(12);

        builder.Property(d => d.ConfirmedAt)
            .HasColumnName("confirmed_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(d => d.RecordedByDoctorId)
            .HasColumnName("recorded_by_doctor_id");

        builder.Property(d => d.RecordedAt)
            .HasColumnName("recorded_at")
            .HasColumnType("timestamp without time zone");
    }
}
