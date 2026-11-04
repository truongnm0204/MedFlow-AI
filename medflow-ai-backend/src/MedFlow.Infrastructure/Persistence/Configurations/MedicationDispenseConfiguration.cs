using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class MedicationDispenseConfiguration : IEntityTypeConfiguration<MedicationDispense>
{
    public void Configure(EntityTypeBuilder<MedicationDispense> builder)
    {
        builder.ToTable("medication_dispenses");

        builder.HasKey(md => md.Id);
        builder.Property(md => md.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(md => md.PrescriptionId)
            .HasColumnName("prescription_id");

        builder.Property(md => md.PreparedByStaffId)
            .HasColumnName("prepared_by_staff_id");

        builder.Property(md => md.DispensedByStaffId)
            .HasColumnName("dispensed_by_staff_id");

        builder.Property(md => md.Status)
            .HasColumnName("status")
            .HasMaxLength(16);

        builder.Property(md => md.PreparedAt)
            .HasColumnName("prepared_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(md => md.DispensedAt)
            .HasColumnName("dispensed_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(md => md.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(100);
    }
}
