using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
{
    public void Configure(EntityTypeBuilder<Prescription> builder)
    {
        builder.ToTable("prescriptions");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(p => p.PrescriptionCode)
            .HasColumnName("prescription_code")
            .HasMaxLength(40);

        builder.Property(p => p.VisitId)
            .HasColumnName("visit_id");

        builder.Property(p => p.PrescribingDoctorId)
            .HasColumnName("prescribing_doctor_id");

        builder.Property(p => p.FulfillmentChoice)
            .HasColumnName("fulfillment_choice")
            .HasMaxLength(18);

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasMaxLength(16);

        builder.Property(p => p.IssuedAt)
            .HasColumnName("issued_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(p => p.ReplacesPrescriptionId)
            .HasColumnName("replaces_prescription_id");
    }
}
