using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public sealed class DiagnosticOrderRoutingHistoryConfiguration : IEntityTypeConfiguration<DiagnosticOrderRoutingHistory>
{
    public void Configure(EntityTypeBuilder<DiagnosticOrderRoutingHistory> builder)
    {
        builder.ToTable("diagnostic_order_routing_history");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(x => x.DiagnosticOrderId)
            .HasColumnName("diagnostic_order_id")
            .IsRequired();

        builder.Property(x => x.VisitId)
            .HasColumnName("visit_id");

        builder.Property(x => x.HospitalId)
            .HasColumnName("hospital_id");

        builder.Property(x => x.FromServicePointId)
            .HasColumnName("from_service_point_id");

        builder.Property(x => x.ToServicePointId)
            .HasColumnName("to_service_point_id")
            .IsRequired();

        builder.Property(x => x.Source)
            .HasColumnName("source")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasColumnName("reason");

        builder.Property(x => x.ChangedByUserId)
            .HasColumnName("changed_by_user_id");

        builder.Property(x => x.ChangedByRole)
            .HasColumnName("changed_by_role")
            .HasMaxLength(32);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp without time zone");

        builder.HasIndex(x => new { x.DiagnosticOrderId, x.CreatedAt })
            .HasDatabaseName("ix_diagnostic_order_routing_history_order");

        builder.HasIndex(x => new { x.VisitId, x.CreatedAt })
            .HasDatabaseName("ix_diagnostic_order_routing_history_visit");

        builder.HasOne<DiagnosticOrder>()
            .WithMany()
            .HasForeignKey(x => x.DiagnosticOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(x => x.VisitId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Hospital>()
            .WithMany()
            .HasForeignKey(x => x.HospitalId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<QueueServicePoint>()
            .WithMany()
            .HasForeignKey(x => x.FromServicePointId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<QueueServicePoint>()
            .WithMany()
            .HasForeignKey(x => x.ToServicePointId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ChangedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
