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

        builder.Property(x => x.SelectionStatus)
            .HasColumnName("selection_status")
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(x => x.DeferredUntilDate)
            .HasColumnName("deferred_until_date")
            .HasColumnType("date");

        builder.Property(x => x.ConfirmedByStaffId)
            .HasColumnName("confirmed_by_staff_id");

        builder.Property(x => x.ConfirmedAt)
            .HasColumnName("confirmed_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.OrderedAt)
            .HasColumnName("ordered_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.AssignedServicePointId)
            .HasColumnName("assigned_service_point_id");

        builder.Property(x => x.SequenceNo)
            .HasColumnName("sequence_no");

        builder.HasIndex(x => new { x.VisitId, x.SequenceNo })
            .HasDatabaseName("ix_diagnostic_orders_visit_sequence");

        builder.Property(x => x.ValidityExtendedUntil)
            .HasColumnName("validity_extended_until")
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.RevalidatedByDoctorId)
            .HasColumnName("revalidated_by_doctor_id");

        builder.Property(x => x.RevalidatedAt)
            .HasColumnName("revalidated_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.RevalidationNote)
            .HasColumnName("revalidation_note");

        builder.HasIndex(x => x.RevalidatedByDoctorId)
            .HasDatabaseName("ix_diagnostic_orders_revalidated_by_doctor_id");

        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(x => x.RevalidatedByDoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(x => x.VisitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
