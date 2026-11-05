using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class StockTransactionConfiguration : IEntityTypeConfiguration<StockTransaction>
{
    public void Configure(EntityTypeBuilder<StockTransaction> builder)
    {
        builder.ToTable("stock_transactions");

        builder.HasKey(st => st.Id);
        builder.Property(st => st.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(st => st.MedicineBatchId)
            .HasColumnName("medicine_batch_id");

        builder.Property(st => st.DispenseItemId)
            .HasColumnName("dispense_item_id");

        builder.Property(st => st.TransactionType)
            .HasColumnName("transaction_type")
            .HasMaxLength(20);

        builder.Property(st => st.QuantityDelta)
            .HasColumnName("quantity_delta")
            .HasColumnType("numeric(14,3)");

        builder.Property(st => st.PerformedByStaffId)
            .HasColumnName("performed_by_staff_id");

        builder.Property(st => st.Reason)
            .HasColumnName("reason");

        builder.Property(st => st.SourceReference)
            .HasColumnName("source_reference")
            .HasMaxLength(120);

        builder.Property(st => st.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp without time zone");
    }
}
