using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class DiagnosticResultVersionConfiguration : IEntityTypeConfiguration<DiagnosticResultVersion>
{
    public void Configure(EntityTypeBuilder<DiagnosticResultVersion> builder)
    {
        builder.ToTable("diagnostic_result_versions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(x => x.DiagnosticResultId)
            .HasColumnName("diagnostic_result_id");

        builder.Property(x => x.VersionNo)
            .HasColumnName("version_no");

        builder.Property(x => x.ResultText)
            .HasColumnName("result_text");

        builder.Property(x => x.Conclusion)
            .HasColumnName("conclusion");

        builder.Property(x => x.ReportFileUrl)
            .HasColumnName("report_file_url");

        builder.Property(x => x.EnteredByStaffId)
            .HasColumnName("entered_by_staff_id");

        builder.Property(x => x.EnteredAt)
            .HasColumnName("entered_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.IsFinalized)
            .HasColumnName("is_finalized");

        builder.Property(x => x.VerifiedByStaffId)
            .HasColumnName("verified_by_staff_id");

        builder.Property(x => x.VerifiedAt)
            .HasColumnName("verified_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.CorrectionReason)
            .HasColumnName("correction_reason");
    }
}
