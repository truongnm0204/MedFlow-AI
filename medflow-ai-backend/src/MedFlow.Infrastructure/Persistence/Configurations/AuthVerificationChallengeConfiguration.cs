using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class AuthVerificationChallengeConfiguration : IEntityTypeConfiguration<AuthVerificationChallenge>
{
    public void Configure(EntityTypeBuilder<AuthVerificationChallenge> builder)
    {
        builder.ToTable("auth_verification_challenges");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(c => c.UserId).HasColumnName("user_id").IsRequired();
        builder.HasIndex(c => c.UserId).HasDatabaseName("ix_auth_verification_challenges_user_id");

        builder.Property(c => c.Purpose)
            .HasColumnName("purpose")
            .HasMaxLength(40)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(c => c.Channel)
            .HasColumnName("channel")
            .HasMaxLength(15)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(c => c.RecipientFingerprint)
            .HasColumnName("recipient_fingerprint")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(c => c.SecretHash).HasColumnName("secret_hash").IsRequired();

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(ChallengeStatus.PENDING);
        builder.HasIndex(c => c.Status).HasDatabaseName("ix_auth_verification_challenges_status");

        builder.Property(c => c.Attempts).HasColumnName("attempts").HasDefaultValue(0);
        builder.Property(c => c.MaxAttempts).HasColumnName("max_attempts").HasDefaultValue(5);
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.HasIndex(c => c.ExpiresAt).HasDatabaseName("ix_auth_verification_challenges_expires_at");

        builder.Property(c => c.VerifiedAt).HasColumnName("verified_at");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
