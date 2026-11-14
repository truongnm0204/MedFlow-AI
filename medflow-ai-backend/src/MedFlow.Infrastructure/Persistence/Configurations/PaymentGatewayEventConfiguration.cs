using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class PaymentGatewayEventConfiguration : IEntityTypeConfiguration<PaymentGatewayEvent>
{
    public void Configure(EntityTypeBuilder<PaymentGatewayEvent> builder)
    {
        builder.ToTable("payment_gateway_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(e => e.Provider).HasColumnName("provider").HasMaxLength(20).IsRequired();
        builder.Property(e => e.ExternalId).HasColumnName("external_id").HasMaxLength(80).IsRequired();
        builder.Property(e => e.PaymentId).HasColumnName("payment_id");
        builder.Property(e => e.PaymentCode).HasColumnName("payment_code").HasMaxLength(40);
        builder.Property(e => e.TransferAmount).HasColumnName("transfer_amount").HasColumnType("numeric(16,2)");
        builder.Property(e => e.ReferenceCode).HasColumnName("reference_code").HasMaxLength(150);
        builder.Property(e => e.Outcome).HasColumnName("outcome").HasMaxLength(24).IsRequired();
        builder.Property(e => e.ReceivedAt)
            .HasColumnName("received_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(e => new { e.Provider, e.ExternalId })
            .IsUnique()
            .HasDatabaseName("ux_payment_gateway_events_provider_external");
        builder.HasIndex(e => e.PaymentId).HasDatabaseName("ix_payment_gateway_events_payment_id");
        builder.HasIndex(e => e.PaymentCode).HasDatabaseName("ix_payment_gateway_events_payment_code");
    }
}
