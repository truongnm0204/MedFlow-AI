using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class VisitChargeConfiguration : IEntityTypeConfiguration<VisitCharge>
{
    public void Configure(EntityTypeBuilder<VisitCharge> builder)
    {
        builder.ToTable("visit_charges");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(c => c.VisitId)
            .HasColumnName("visit_id");

        builder.Property(c => c.ServiceId)
            .HasColumnName("service_id");

        builder.Property(c => c.DiagnosticOrderId)
            .HasColumnName("diagnostic_order_id");

        builder.Property(c => c.PrescriptionId)
            .HasColumnName("prescription_id");

        builder.Property(c => c.PrescriptionItemId)
            .HasColumnName("prescription_item_id");

        builder.Property(c => c.MedicineId)
            .HasColumnName("medicine_id");

        builder.Property(c => c.Quantity)
            .HasColumnName("quantity")
            .HasColumnType("numeric(14,3)");

        builder.Property(c => c.MedicineUnitPrice)
            .HasColumnName("medicine_unit_price")
            .HasColumnType("numeric(14,2)");

        builder.Property(c => c.UnitPrice)
            .HasColumnName("unit_price")
            .HasColumnType("numeric(14,2)");

        builder.Property(c => c.PaymentRequirement)
            .HasColumnName("payment_requirement")
            .HasMaxLength(12);

        builder.Property(c => c.ChargeType)
            .HasColumnName("charge_type")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(c => c.CancelledAt)
            .HasColumnName("cancelled_at")
            .HasColumnType("timestamp without time zone");

        builder.HasIndex(c => c.VisitId)
            .HasDatabaseName("uq_visit_charges_active_exam_per_visit")
            .IsUnique()
            .HasFilter("\"charge_type\" = 'EXAMINATION' AND \"status\" = 'ACTIVE'");

        builder.HasIndex(c => c.DiagnosticOrderId)
            .HasDatabaseName("uq_visit_charges_diagnostic_order")
            .IsUnique()
            .HasFilter("\"diagnostic_order_id\" IS NOT NULL");

        builder.HasIndex(c => c.PrescriptionItemId)
            .HasDatabaseName("uq_visit_charges_prescription_item")
            .IsUnique()
            .HasFilter("\"prescription_item_id\" IS NOT NULL");

        builder.HasIndex(c => c.PrescriptionId)
            .HasDatabaseName("ix_visit_charges_prescription_id");

        builder.HasIndex(c => c.MedicineId)
            .HasDatabaseName("ix_visit_charges_medicine_id");

        builder.HasIndex(c => new { c.VisitId, c.ChargeType })
            .HasDatabaseName("ix_visit_charges_visit_type");

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(c => c.VisitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DiagnosticOrder>()
            .WithMany()
            .HasForeignKey(c => c.DiagnosticOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Prescription>()
            .WithMany()
            .HasForeignKey(c => c.PrescriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PrescriptionItem>()
            .WithMany()
            .HasForeignKey(c => c.PrescriptionItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(c => c.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
