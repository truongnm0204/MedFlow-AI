using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class SpecialtyConfiguration : IEntityTypeConfiguration<Specialty>
{
    public void Configure(EntityTypeBuilder<Specialty> builder)
    {
        builder.ToTable("specialties");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(s => s.SpecialtyCode)
            .HasColumnName("specialty_code")
            .HasMaxLength(40);

        builder.Property(s => s.Name)
            .HasColumnName("name")
            .HasMaxLength(200);

        builder.Property(s => s.IsActive)
            .HasColumnName("is_active");

        builder.HasIndex(s => s.SpecialtyCode)
            .IsUnique()
            .HasDatabaseName("uq_specialties_specialty_code");
    }
}
