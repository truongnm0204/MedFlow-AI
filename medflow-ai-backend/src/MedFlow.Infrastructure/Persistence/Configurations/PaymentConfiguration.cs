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

        builder.Property(p => p.VisitId)
            .HasColumnName("visit_id");

        builder.Property(p => p.Amount)
            .HasColumnName("amount")
            .HasColumnType("numeric(16,2)");

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasMaxLength(16);

        builder.Property(p => p.PaidAt)
            .HasColumnName("paid_at")
            .HasColumnType("timestamp without time zone");
    }
}
