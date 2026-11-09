using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class DiagnosticOrderConfiguration : IEntityTypeConfiguration<DiagnosticOrder>
{
    public void Configure(EntityTypeBuilder<DiagnosticOrder> builder)
    {
        builder.ToTable("diagnostic_orders");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(x => x.OrderCode)
            .HasColumnName("order_code")
            .HasMaxLength(40);

        builder.Property(x => x.VisitId)
            .HasColumnName("visit_id");

        builder.Property(x => x.OrderingDoctorId)
            .HasColumnName("ordering_doctor_id");

        builder.Property(x => x.ServiceId)
            .HasColumnName("service_id");

        builder.Property(x => x.QueueEntryId)
            .HasColumnName("queue_entry_id");

        builder.Property(x => x.Instruction)
            .HasColumnName("instruction");

        builder.Property(x => x.RequiredForConclusion)
            .HasColumnName("required_for_conclusion");

        builder.Property(x => x.Priority)
            .HasColumnName("priority")
            .HasMaxLength(16);

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(20);

        builder.Property(x => x.ConfirmedByStaffId)
            .HasColumnName("confirmed_by_staff_id");

        builder.Property(x => x.ConfirmedAt)
            .HasColumnName("confirmed_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.OrderedAt)
            .HasColumnName("ordered_at")
            .HasColumnType("timestamp without time zone");
    }
}
