using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class HospitalConfiguration : IEntityTypeConfiguration<Hospital>
{
    public void Configure(EntityTypeBuilder<Hospital> builder)
    {
        builder.ToTable("hospitals");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(h => h.HospitalCode)
            .HasColumnName("hospital_code")
            .HasMaxLength(40);

        builder.Property(h => h.Name)
            .HasColumnName("name")
            .HasMaxLength(255);

        builder.Property(h => h.Address)
            .HasColumnName("address");

        builder.Property(h => h.IsActive)
            .HasColumnName("is_active");
    }
}
