using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class MedicineBatchConfiguration : IEntityTypeConfiguration<MedicineBatch>
{
    public void Configure(EntityTypeBuilder<MedicineBatch> builder)
    {
        builder.ToTable("medicine_batches");

        builder.HasKey(mb => mb.Id);
        builder.Property(mb => mb.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(mb => mb.MedicineId)
            .HasColumnName("medicine_id");

        builder.Property(mb => mb.BatchNumber)
            .HasColumnName("batch_number")
            .HasMaxLength(80);

        builder.Property(mb => mb.ReceivedAt)
            .HasColumnName("received_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(mb => mb.ExpiryDate)
            .HasColumnName("expiry_date")
            .HasColumnType("date");

        builder.Property(mb => mb.QuantityOnHand)
            .HasColumnName("quantity_on_hand")
            .HasColumnType("numeric(14,3)");

        builder.Property(mb => mb.IsBlocked)
            .HasColumnName("is_blocked");
    }
}
