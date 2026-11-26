using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.ToTable("payment_allocations");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(x => x.PaymentId)
            .HasColumnName("payment_id")
            .IsRequired();

        builder.Property(x => x.VisitChargeId)
            .HasColumnName("visit_charge_id")
            .IsRequired();

        builder.Property(x => x.VisitId)
            .HasColumnName("visit_id")
            .IsRequired();

        builder.Property(x => x.AllocatedAmount)
            .HasColumnName("allocated_amount")
            .HasColumnType("numeric(14,2)")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        builder.HasIndex(x => new { x.PaymentId, x.VisitChargeId })
            .IsUnique()
            .HasDatabaseName("uq_payment_allocations_payment_charge");

        builder.HasIndex(x => x.VisitId)
            .HasDatabaseName("ix_payment_allocations_visit_id");

        builder.HasIndex(x => x.VisitChargeId)
            .HasDatabaseName("ix_payment_allocations_visit_charge_id");

        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<VisitCharge>()
            .WithMany()
            .HasForeignKey(x => x.VisitChargeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(x => x.VisitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
