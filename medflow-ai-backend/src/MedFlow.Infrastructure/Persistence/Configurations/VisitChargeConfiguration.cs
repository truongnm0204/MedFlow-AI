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

        builder.Property(c => c.UnitPrice)
            .HasColumnName("unit_price")
            .HasColumnType("numeric(14,2)");

        builder.Property(c => c.PaymentRequirement)
            .HasColumnName("payment_requirement")
            .HasMaxLength(12);
    }
}
