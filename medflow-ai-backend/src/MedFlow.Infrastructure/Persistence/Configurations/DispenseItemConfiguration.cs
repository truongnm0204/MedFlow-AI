using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class DispenseItemConfiguration : IEntityTypeConfiguration<DispenseItem>
{
    public void Configure(EntityTypeBuilder<DispenseItem> builder)
    {
        builder.ToTable("dispense_items");

        builder.HasKey(di => di.Id);
        builder.Property(di => di.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(di => di.DispenseId)
            .HasColumnName("dispense_id");

        builder.Property(di => di.PrescriptionItemId)
            .HasColumnName("prescription_item_id");

        builder.Property(di => di.MedicineBatchId)
            .HasColumnName("medicine_batch_id");

        builder.Property(di => di.PlannedQuantity)
            .HasColumnName("planned_quantity")
            .HasColumnType("numeric(14,3)");

        builder.Property(di => di.DispensedQuantity)
            .HasColumnName("dispensed_quantity")
            .HasColumnType("numeric(14,3)");

        builder.Property(di => di.ShortageReason)
            .HasColumnName("shortage_reason");
    }
}
