using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class MedicalRecordConfiguration : IEntityTypeConfiguration<MedicalRecord>
{
    public void Configure(EntityTypeBuilder<MedicalRecord> builder)
    {
        builder.ToTable("medical_records");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(m => m.VisitId)
            .HasColumnName("visit_id");

        builder.Property(m => m.CreatedByDoctorId)
            .HasColumnName("created_by_doctor_id");

        builder.Property(m => m.Status)
            .HasColumnName("status")
            .HasMaxLength(16);

        builder.Property(m => m.FinalizedAt)
            .HasColumnName("finalized_at")
            .HasColumnType("timestamp without time zone");
    }
}
