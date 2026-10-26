using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("patients");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(p => p.PatientCode).HasColumnName("patient_code").HasMaxLength(40).IsRequired();
        builder.HasIndex(p => p.PatientCode).IsUnique().HasDatabaseName("uq_patients_patient_code");

        builder.Property(p => p.UserId).HasColumnName("user_id");
        // FK + one-to-one with User is configured in UserConfiguration.

        builder.Property(p => p.FullName).HasColumnName("full_name").HasMaxLength(200).IsRequired();
        builder.Property(p => p.DateOfBirth).HasColumnName("date_of_birth");
        builder.Property(p => p.Gender).HasColumnName("gender").HasMaxLength(16);
        builder.Property(p => p.Phone).HasColumnName("phone").HasMaxLength(30);
        builder.Property(p => p.Email).HasColumnName("email").HasMaxLength(255);

        builder.Property(p => p.IdentificationHash).HasColumnName("identification_hash").HasMaxLength(128);
        builder.HasIndex(p => p.IdentificationHash)
            .IsUnique()
            .HasDatabaseName("uq_patients_identification_hash");

        builder.Property(p => p.Address).HasColumnName("address");
        builder.Property(p => p.Status).HasColumnName("status").HasMaxLength(16);
        builder.Property(p => p.BloodType).HasColumnName("blood_type").HasMaxLength(8);
    }
}
