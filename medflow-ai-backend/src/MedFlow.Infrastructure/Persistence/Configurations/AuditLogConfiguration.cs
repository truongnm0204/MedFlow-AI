using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(a => a.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(a => a.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityType).HasColumnName("entity_type").HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityId).HasColumnName("entity_id");

        builder.Property(a => a.OldValue).HasColumnName("old_value").HasColumnType("jsonb");
        builder.Property(a => a.NewValue).HasColumnName("new_value").HasColumnType("jsonb");
        builder.Property(a => a.Reason).HasColumnName("reason");

        builder.Property(a => a.IpAddress).HasColumnName("ip_address").HasColumnType("inet");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        builder.HasIndex(a => new { a.EntityType, a.EntityId }).HasDatabaseName("idx_audit_entity");

        builder.HasOne(a => a.ActorUser)
            .WithMany()
            .HasForeignKey(a => a.ActorUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
