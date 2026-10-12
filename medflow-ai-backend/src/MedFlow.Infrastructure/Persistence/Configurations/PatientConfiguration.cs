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
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.PatientCode).HasColumnName("patient_code").HasMaxLength(40).IsRequired();
        builder.HasIndex(p => p.PatientCode).IsUnique();

        builder.Property(p => p.UserId).HasColumnName("user_id");
        // Unique index trên user_id do one-to-one UserConfiguration tự tạo — không lặp lại ở đây.

        builder.Property(p => p.FullName).HasColumnName("full_name").HasMaxLength(255).IsRequired();
        builder.Property(p => p.DateOfBirth).HasColumnName("date_of_birth");
        builder.Property(p => p.Gender).HasColumnName("gender").HasMaxLength(20);
        builder.Property(p => p.Phone).HasColumnName("phone").HasMaxLength(30);
        builder.Property(p => p.Email).HasColumnName("email").HasMaxLength(255);

        builder.Property(p => p.IdentificationNo).HasColumnName("identification_no").HasMaxLength(50);
        builder.HasIndex(p => p.IdentificationNo)
            .IsUnique()
            .HasFilter("identification_no IS NOT NULL")
            .HasDatabaseName("uq_patients_identification");

        builder.Property(p => p.Address).HasColumnName("address");
        builder.Property(p => p.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("ACTIVE");

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.Property(p => p.AvatarUrl).HasColumnName("avatar_url");
        builder.Property(p => p.Nationality).HasColumnName("nationality").HasMaxLength(100);
        builder.Property(p => p.BloodType).HasColumnName("blood_type").HasMaxLength(5);
        builder.Property(p => p.EmergencyContactName).HasColumnName("emergency_contact_name").HasMaxLength(255);
        builder.Property(p => p.EmergencyContactPhone).HasColumnName("emergency_contact_phone").HasMaxLength(30);
        builder.Property(p => p.EmergencyContactRelationship).HasColumnName("emergency_contact_relationship").HasMaxLength(60);
        builder.Property(p => p.IdentityIssuedDate).HasColumnName("identity_issued_date");
        builder.Property(p => p.IdentityIssuedPlace).HasColumnName("identity_issued_place").HasMaxLength(255);
    }
}
