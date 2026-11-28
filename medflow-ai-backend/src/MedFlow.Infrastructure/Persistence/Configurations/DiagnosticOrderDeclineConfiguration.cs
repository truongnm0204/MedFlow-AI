using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public sealed class DiagnosticOrderDeclineConfiguration : IEntityTypeConfiguration<DiagnosticOrderDecline>
{
    public void Configure(EntityTypeBuilder<DiagnosticOrderDecline> builder)
    {
        builder.ToTable("diagnostic_order_declines");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(x => x.DiagnosticOrderId)
            .HasColumnName("diagnostic_order_id")
            .IsRequired();

        builder.Property(x => x.VisitId)
            .HasColumnName("visit_id")
            .IsRequired();

        builder.Property(x => x.HospitalId)
            .HasColumnName("hospital_id");

        builder.Property(x => x.RecordedByUserId)
            .HasColumnName("recorded_by_user_id")
            .IsRequired();

        builder.Property(x => x.RecordedByRole)
            .HasColumnName("recorded_by_role")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Source)
            .HasColumnName("source")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasColumnName("reason")
            .IsRequired();

        builder.Property(x => x.NotifiedDoctorId)
            .HasColumnName("notified_doctor_id");

        builder.Property(x => x.NotifiedDoctorUserId)
            .HasColumnName("notified_doctor_user_id");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp without time zone");

        builder.HasIndex(x => new { x.DiagnosticOrderId, x.CreatedAt })
            .HasDatabaseName("ix_diagnostic_order_declines_order");

        builder.HasIndex(x => new { x.VisitId, x.CreatedAt })
            .HasDatabaseName("ix_diagnostic_order_declines_visit");

        builder.HasOne<DiagnosticOrder>()
            .WithMany()
            .HasForeignKey(x => x.DiagnosticOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(x => x.VisitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(x => x.NotifiedDoctorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.NotifiedDoctorUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RecordedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
