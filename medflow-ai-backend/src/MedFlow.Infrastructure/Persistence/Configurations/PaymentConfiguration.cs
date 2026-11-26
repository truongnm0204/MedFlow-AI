using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(p => p.PaymentCode)
            .HasColumnName("payment_code")
            .HasMaxLength(40);

        builder.Property(p => p.VisitId)
            .HasColumnName("visit_id");

        builder.Property(p => p.VisitChargeId)
            .HasColumnName("visit_charge_id");

        builder.Property(p => p.PaymentScope)
            .HasColumnName("payment_scope")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(p => p.PaymentMethod)
            .HasColumnName("payment_method")
            .HasMaxLength(16);

        builder.Property(p => p.CollectedByStaffId)
            .HasColumnName("collected_by_staff_id");

        builder.Property(p => p.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(100);

        builder.Property(p => p.Amount)
            .HasColumnName("amount")
            .HasColumnType("numeric(16,2)");

        builder.Property(p => p.ExternalReference)
            .HasColumnName("external_reference")
            .HasMaxLength(150);

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasMaxLength(16);

        builder.Property(p => p.PaidAt)
            .HasColumnName("paid_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp without time zone");

        builder.HasIndex(p => p.VisitChargeId)
            .HasDatabaseName("ix_payments_visit_charge_id");

        builder.HasIndex(p => p.CollectedByStaffId)
            .HasDatabaseName("ix_payments_collected_by_staff_id");

        builder.HasIndex(p => p.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_payments_idempotency_key")
            .HasFilter("\"idempotency_key\" IS NOT NULL");

        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(p => p.CollectedByStaffId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(p => p.VisitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<VisitCharge>()
            .WithMany()
            .HasForeignKey(p => p.VisitChargeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
