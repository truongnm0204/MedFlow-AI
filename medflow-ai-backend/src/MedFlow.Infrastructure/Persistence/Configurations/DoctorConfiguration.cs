using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        builder.ToTable("doctors");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(d => d.StaffId).HasColumnName("staff_id");
        builder.Property(d => d.HospitalId).HasColumnName("hospital_id");
        builder.Property(d => d.DoctorCode).HasColumnName("doctor_code").HasMaxLength(40).IsRequired();
        builder.Property(d => d.SpecialtyId).HasColumnName("specialty_id");
        builder.Property(d => d.ProfessionalTitle).HasColumnName("professional_title").HasMaxLength(120);
        builder.Property(d => d.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(d => d.ProfileImageUrl).HasColumnName("profile_image_url");
        builder.Property(d => d.YearsOfExperience).HasColumnName("years_of_experience");
        builder.Property(d => d.LicenseNumber).HasColumnName("license_number").HasMaxLength(100);
        builder.Property(d => d.ProfessionalBio).HasColumnName("professional_bio");
        builder.Property(d => d.QualificationsSummary).HasColumnName("qualifications_summary");

        builder.HasIndex(d => d.DoctorCode)
            .IsUnique()
            .HasDatabaseName("uq_doctors_doctor_code");

        builder.HasOne(d => d.Staff)
            .WithOne(s => s.Doctor)
            .HasForeignKey<Doctor>(d => d.StaffId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(d => d.Specialty)
            .WithMany()
            .HasForeignKey(d => d.SpecialtyId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

