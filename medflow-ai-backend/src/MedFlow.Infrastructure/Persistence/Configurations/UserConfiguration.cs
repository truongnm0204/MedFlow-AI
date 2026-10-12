using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(u => u.Username).HasColumnName("username").HasMaxLength(80).IsRequired();
        builder.HasIndex(u => u.Username).IsUnique();

        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.PasswordHash).HasColumnName("password_hash");
        builder.Property(u => u.AuthProvider).HasColumnName("auth_provider").HasMaxLength(30).HasDefaultValue("LOCAL");
        builder.Property(u => u.ProviderSubject).HasColumnName("provider_subject").HasMaxLength(255);

        builder.Property(u => u.AccountStatus)
            .HasColumnName("account_status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(AccountStatus.ACTIVE);

        builder.Property(u => u.EmailVerifiedAt).HasColumnName("email_verified_at");
        builder.Property(u => u.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(u => u.FailedLoginAttempts).HasColumnName("failed_login_attempts").HasDefaultValue(0);
        builder.Property(u => u.LockedUntil).HasColumnName("locked_until");

        builder.Property(u => u.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasOne(u => u.Patient)
            .WithOne(p => p.User)
            .HasForeignKey<Patient>(p => p.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(u => u.UserRoles)
            .WithOne(ur => ur.User)
            .HasForeignKey(ur => ur.UserId);

        builder.HasMany(u => u.RefreshTokens)
            .WithOne(rt => rt.User)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
