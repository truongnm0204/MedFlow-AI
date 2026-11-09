using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class ClinicalAddendumConfiguration : IEntityTypeConfiguration<ClinicalAddendum>
{
    public void Configure(EntityTypeBuilder<ClinicalAddendum> builder)
    {
        builder.ToTable("clinical_addenda");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(x => x.VisitId)
            .HasColumnName("visit_id");

        builder.Property(x => x.RecordType)
            .HasColumnName("record_type")
            .HasMaxLength(20);

        builder.Property(x => x.TargetRecordId)
            .HasColumnName("target_record_id");

        builder.Property(x => x.CorrectedContent)
            .HasColumnName("corrected_content")
            .HasColumnType("jsonb");

        builder.Property(x => x.Reason)
            .HasColumnName("reason");

        builder.Property(x => x.AuthorDoctorId)
            .HasColumnName("author_doctor_id");

        builder.Property(x => x.RecordedAt)
            .HasColumnName("recorded_at")
            .HasColumnType("timestamp without time zone");
    }
}
