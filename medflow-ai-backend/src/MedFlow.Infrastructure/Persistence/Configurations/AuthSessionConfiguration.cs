using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("auth_sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
        builder.HasIndex(s => s.UserId).HasDatabaseName("ix_auth_sessions_user_id");

        builder.Property(s => s.RefreshTokenHash).HasColumnName("refresh_token_hash").IsRequired();

        builder.Property(s => s.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(s => s.RevokedAt).HasColumnName("revoked_at");

        // Columns added back via migration 005
        builder.Property(s => s.ReplacedByTokenHash).HasColumnName("replaced_by_token_hash").HasMaxLength(128);
        builder.Property(s => s.CreatedByIp).HasColumnName("created_by_ip").HasMaxLength(45);
        builder.Property(s => s.RevokedByIp).HasColumnName("revoked_by_ip").HasMaxLength(45);
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        builder.Ignore(s => s.IsActive);
    }
}
