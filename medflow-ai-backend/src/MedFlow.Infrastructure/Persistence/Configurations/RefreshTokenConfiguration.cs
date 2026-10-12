using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(rt => rt.Id);
        builder.Property(rt => rt.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(rt => rt.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(rt => rt.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
        builder.HasIndex(rt => rt.TokenHash).IsUnique();

        builder.Property(rt => rt.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(rt => rt.RevokedAt).HasColumnName("revoked_at");
        builder.Property(rt => rt.ReplacedByTokenHash).HasColumnName("replaced_by_token_hash").HasMaxLength(128);
        builder.Property(rt => rt.CreatedByIp).HasColumnName("created_by_ip").HasMaxLength(45);
        builder.Property(rt => rt.RevokedByIp).HasColumnName("revoked_by_ip").HasMaxLength(45);
        builder.Property(rt => rt.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        builder.HasIndex(rt => rt.UserId)
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("idx_refresh_tokens_active");

        builder.Ignore(rt => rt.IsActive);
    }
}
