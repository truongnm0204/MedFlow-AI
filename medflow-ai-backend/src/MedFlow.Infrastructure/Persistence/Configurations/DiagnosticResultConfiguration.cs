using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class DiagnosticResultConfiguration : IEntityTypeConfiguration<DiagnosticResult>
{
    public void Configure(EntityTypeBuilder<DiagnosticResult> builder)
    {
        builder.ToTable("diagnostic_results");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(x => x.DiagnosticOrderId)
            .HasColumnName("diagnostic_order_id");

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(16);

        builder.Property(x => x.FinalizedAt)
            .HasColumnName("finalized_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.FinalizedByStaffId)
            .HasColumnName("finalized_by_staff_id");
    }
}
