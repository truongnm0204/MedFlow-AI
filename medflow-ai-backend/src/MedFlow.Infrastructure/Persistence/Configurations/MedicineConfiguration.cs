using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class MedicineConfiguration : IEntityTypeConfiguration<Medicine>
{
    public void Configure(EntityTypeBuilder<Medicine> builder)
    {
        builder.ToTable("medicines");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(m => m.HospitalId)
            .HasColumnName("hospital_id");

        builder.Property(m => m.MedicineCode)
            .HasColumnName("medicine_code")
            .HasMaxLength(40);

        builder.Property(m => m.Name)
            .HasColumnName("name")
            .HasMaxLength(200);

        builder.Property(m => m.ActiveIngredient)
            .HasColumnName("active_ingredient")
            .HasMaxLength(200);

        builder.Property(m => m.Strength)
            .HasColumnName("strength")
            .HasMaxLength(100);

        builder.Property(m => m.DosageForm)
            .HasColumnName("dosage_form")
            .HasMaxLength(100);

        builder.Property(m => m.DispensingUnit)
            .HasColumnName("dispensing_unit")
            .HasMaxLength(40);

        builder.Property(m => m.ContentQuantity)
            .HasColumnName("content_quantity")
            .HasColumnType("numeric(14,3)");

        builder.Property(m => m.ContentUnit)
            .HasColumnName("content_unit")
            .HasMaxLength(40);

        builder.Property(m => m.IsActive)
            .HasColumnName("is_active");
    }
}
