using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class ServicePriceConfiguration : IEntityTypeConfiguration<ServicePrice>
{
    public void Configure(EntityTypeBuilder<ServicePrice> builder)
    {
        builder.ToTable("service_prices");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(p => p.ServiceId)
            .HasColumnName("service_id");

        builder.Property(p => p.PriceAmount)
            .HasColumnName("price_amount")
            .HasPrecision(14, 2);

        builder.Property(p => p.EffectiveFrom)
            .HasColumnName("effective_from")
            .HasColumnType("timestamp without time zone");

        builder.Property(p => p.EffectiveTo)
            .HasColumnName("effective_to")
            .HasColumnType("timestamp without time zone");
    }
}
