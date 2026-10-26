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
            .UseIdentityByDefaultColumn();

        builder.Property(a => a.ActorUserId).HasColumnName("actor_user_id");
        builder.HasIndex(a => a.ActorUserId).HasDatabaseName("ix_audit_logs_actor_user_id");

        builder.Property(a => a.HospitalId).HasColumnName("hospital_id");
        builder.HasIndex(a => a.HospitalId).HasDatabaseName("ix_audit_logs_hospital_id");

        builder.Property(a => a.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityType).HasColumnName("entity_type").HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityId).HasColumnName("entity_id").HasMaxLength(80);

        builder.Property(a => a.Reason).HasColumnName("reason");
        builder.Property(a => a.ChangeData).HasColumnName("change_data").HasColumnType("jsonb");

        // Re-added via migration 005
        builder.Property(a => a.IpAddress).HasColumnName("ip_address").HasColumnType("inet");
        builder.HasIndex(a => a.IpAddress).HasDatabaseName("ix_audit_logs_ip_address");

        builder.Property(a => a.OccurredAt).HasColumnName("occurred_at");

        builder.HasOne(a => a.ActorUser)
            .WithMany()
            .HasForeignKey(a => a.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
