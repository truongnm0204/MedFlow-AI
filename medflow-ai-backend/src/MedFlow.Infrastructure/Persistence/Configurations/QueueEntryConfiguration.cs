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

        builder.Property(q => q.AppointmentId)
            .HasColumnName("appointment_id");

        builder.Property(q => q.QueueNumber)
            .HasColumnName("queue_number");

        builder.Property(q => q.QueueType)
            .HasColumnName("queue_type")
            .HasMaxLength(20);

        builder.Property(q => q.CalledAt)
            .HasColumnName("called_at");

        builder.Property(q => q.StartedAt)
            .HasColumnName("started_at");
    }
}
