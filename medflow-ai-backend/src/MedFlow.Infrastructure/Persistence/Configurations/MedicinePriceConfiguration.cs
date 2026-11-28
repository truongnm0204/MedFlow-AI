using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class MedicinePriceConfiguration : IEntityTypeConfiguration<MedicinePrice>
{
    public void Configure(EntityTypeBuilder<MedicinePrice> builder)
    {
        builder.ToTable("medicine_prices");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(p => p.HospitalId)
            .HasColumnName("hospital_id")
            .IsRequired();

        builder.Property(p => p.MedicineId)
            .HasColumnName("medicine_id")
            .IsRequired();

        builder.Property(p => p.PriceAmount)
            .HasColumnName("price_amount")
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(p => p.EffectiveFrom)
            .HasColumnName("effective_from")
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        builder.Property(p => p.EffectiveTo)
            .HasColumnName("effective_to")
            .HasColumnType("timestamp without time zone");

        builder.Property(p => p.DecisionNumber)
            .HasColumnName("decision_number")
            .HasMaxLength(100);

        builder.Property(p => p.AppliedBy)
            .HasColumnName("applied_by")
            .HasMaxLength(150);

        builder.Property(p => p.Note)
            .HasColumnName("note")
            .HasMaxLength(500);

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(p => p.CreatedByUserId)
            .HasColumnName("created_by_user_id");

        builder.HasOne(p => p.Medicine)
            .WithMany(m => m.MedicinePrices)
            .HasForeignKey(p => p.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.MedicineId, p.EffectiveFrom })
            .HasDatabaseName("ix_medicine_prices_effective_lookup");

        builder.HasIndex(p => p.HospitalId)
            .HasDatabaseName("ix_medicine_prices_hospital_id");
    }
}
