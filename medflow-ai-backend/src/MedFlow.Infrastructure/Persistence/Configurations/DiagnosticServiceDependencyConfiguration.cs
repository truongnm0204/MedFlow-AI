using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public sealed class DiagnosticServiceDependencyConfiguration : IEntityTypeConfiguration<DiagnosticServiceDependency>
{
    public void Configure(EntityTypeBuilder<DiagnosticServiceDependency> builder)
    {
        builder.ToTable("diagnostic_service_dependencies");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(x => x.HospitalId)
            .HasColumnName("hospital_id");

        builder.Property(x => x.ServiceId)
            .HasColumnName("service_id")
            .IsRequired();

        builder.Property(x => x.PrerequisiteServiceId)
            .HasColumnName("prerequisite_service_id")
            .IsRequired();

        builder.Property(x => x.Note)
            .HasColumnName("note")
            .HasMaxLength(500);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp without time zone");

        builder.HasIndex(x => new { x.ServiceId, x.PrerequisiteServiceId })
            .IsUnique()
            .HasDatabaseName("uq_diagnostic_service_dependencies_pair");

        builder.HasIndex(x => x.PrerequisiteServiceId)
            .HasDatabaseName("ix_diagnostic_service_dependencies_prerequisite");

        builder.HasOne<Service>()
            .WithMany()
            .HasForeignKey(x => x.ServiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Service>()
            .WithMany()
            .HasForeignKey(x => x.PrerequisiteServiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Hospital>()
            .WithMany()
            .HasForeignKey(x => x.HospitalId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
