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
            .UseIdentityByDefaultColumn();

        builder.Property(r => r.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.HasIndex(r => r.Code).IsUnique().HasDatabaseName("uq_roles_code");

        builder.Property(r => r.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
    }
}
