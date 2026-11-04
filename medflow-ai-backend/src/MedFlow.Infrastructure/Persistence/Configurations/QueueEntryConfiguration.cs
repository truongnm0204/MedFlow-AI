using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class QueueEntryConfiguration : IEntityTypeConfiguration<QueueEntry>
{
    public void Configure(EntityTypeBuilder<QueueEntry> builder)
    {
        builder.ToTable("queue_entries");

        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(q => q.VisitId)
            .HasColumnName("visit_id");

        builder.Property(q => q.AppointmentId)
            .HasColumnName("appointment_id");

        builder.Property(q => q.ServicePointId)
            .HasColumnName("service_point_id");

        builder.Property(q => q.CurrentServicePointId)
            .HasColumnName("current_service_point_id");

        builder.Property(q => q.QueueDate)
            .HasColumnName("queue_date");

        builder.Property(q => q.QueueNumber)
            .HasColumnName("queue_number");

        builder.Property(q => q.QueueType)
            .HasColumnName("queue_type")
            .HasMaxLength(20);

        builder.Property(q => q.Status)
            .HasColumnName("status")
            .HasMaxLength(20);

        builder.Property(q => q.ScheduledAt)
            .HasColumnName("scheduled_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(q => q.PresenceStatus)
            .HasColumnName("presence_status")
            .HasMaxLength(16);

        builder.Property(q => q.ArrivedAt)
            .HasColumnName("arrived_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(q => q.ArrivalClass)
            .HasColumnName("arrival_class")
            .HasMaxLength(20);

        builder.Property(q => q.ManualPriority)
            .HasColumnName("manual_priority")
            .HasMaxLength(10);

        builder.Property(q => q.StateVersion)
            .HasColumnName("state_version");

        builder.Property(q => q.ReadyAt)
            .HasColumnName("ready_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(q => q.CalledAt)
            .HasColumnName("called_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(q => q.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(q => q.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(q => q.BlockedReason)
            .HasColumnName("blocked_reason");
    }
}
