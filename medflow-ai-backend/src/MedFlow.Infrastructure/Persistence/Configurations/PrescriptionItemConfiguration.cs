using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class PrescriptionItemConfiguration : IEntityTypeConfiguration<PrescriptionItem>
{
    public void Configure(EntityTypeBuilder<PrescriptionItem> builder)
    {
        builder.ToTable("prescription_items");

        builder.HasKey(pi => pi.Id);
        builder.Property(pi => pi.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(pi => pi.PrescriptionId)
            .HasColumnName("prescription_id");

        builder.Property(pi => pi.MedicineId)
            .HasColumnName("medicine_id");

        builder.Property(pi => pi.PrescribedQuantity)
            .HasColumnName("prescribed_quantity")
            .HasColumnType("numeric(14,3)");

        builder.Property(pi => pi.SingleDoseQuantity)
            .HasColumnName("single_dose_quantity")
            .HasColumnType("numeric(14,3)");

        builder.Property(pi => pi.SingleDoseUnit)
            .HasColumnName("single_dose_unit")
            .HasMaxLength(40);

        builder.Property(pi => pi.DoseScheduleText)
            .HasColumnName("dose_schedule_text");

        builder.Property(pi => pi.Frequency)
            .HasColumnName("frequency")
            .HasMaxLength(160);

        builder.Property(pi => pi.DurationDays)
            .HasColumnName("duration_days");

        builder.Property(pi => pi.AdministrationRoute)
            .HasColumnName("administration_route")
            .HasMaxLength(100);

        builder.Property(pi => pi.Instructions)
            .HasColumnName("instructions");
    }
}
