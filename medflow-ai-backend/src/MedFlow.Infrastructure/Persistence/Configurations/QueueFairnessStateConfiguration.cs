using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class QueueFairnessStateConfiguration : IEntityTypeConfiguration<QueueFairnessState>
{
    public void Configure(EntityTypeBuilder<QueueFairnessState> builder)
    {
        builder.ToTable("queue_fairness_states");

        builder.HasKey(s => new { s.ServicePointId, s.QueueDate });

        builder.Property(s => s.ServicePointId)
            .HasColumnName("service_point_id");

        builder.Property(s => s.QueueDate)
            .HasColumnName("queue_date");

        builder.Property(s => s.ConsecutivePriorityStarted)
            .HasColumnName("consecutive_priority_started")
            .IsConcurrencyToken();

        builder.Property(s => s.LastStartedQueueEntryId)
            .HasColumnName("last_started_queue_entry_id")
            .IsConcurrencyToken();

        builder.Property(s => s.PolicyVersion)
            .HasColumnName("policy_version");
    }
}
