using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class HospitalPolicyConfiguration : IEntityTypeConfiguration<HospitalPolicy>
{
    public void Configure(EntityTypeBuilder<HospitalPolicy> builder)
    {
        builder.ToTable("hospital_policies");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(p => p.HospitalId).HasColumnName("hospital_id");
        builder.HasIndex(p => p.HospitalId).HasDatabaseName("ix_hospital_policies_hospital_id");

        builder.Property(p => p.QueueServicePointId).HasColumnName("queue_service_point_id");
        builder.HasIndex(p => p.QueueServicePointId).HasDatabaseName("ix_hospital_policies_queue_service_point_id");

        builder.Property(p => p.PolicyType).HasColumnName("policy_type").HasMaxLength(20);
        builder.Property(p => p.PolicyKey).HasColumnName("policy_key").HasMaxLength(100);
        builder.Property(p => p.PolicyValue).HasColumnName("policy_value").HasColumnType("jsonb");
        builder.Property(p => p.PolicyVersion).HasColumnName("policy_version");
        builder.Property(p => p.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(p => p.EffectiveTo).HasColumnName("effective_to");
    }
}
