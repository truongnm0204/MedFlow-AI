using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.HasIndex(r => r.Code).IsUnique();

        builder.Property(r => r.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(r => r.IsActive).HasColumnName("is_active").HasDefaultValue(true);
    }
}
