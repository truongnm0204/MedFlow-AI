using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class VisitConfiguration : IEntityTypeConfiguration<Visit>
{
    public void Configure(EntityTypeBuilder<Visit> builder)
    {
        builder.ToTable("visits");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(v => v.VisitCode)
            .HasColumnName("visit_code")
            .HasMaxLength(40);

        builder.Property(v => v.PatientId)
            .HasColumnName("patient_id");

        builder.Property(v => v.AppointmentId)
            .HasColumnName("appointment_id");

        builder.Property(v => v.AssignedDoctorId)
            .HasColumnName("assigned_doctor_id");

        builder.Property(v => v.ExaminationRoomId)
            .HasColumnName("examination_room_id");

        builder.Property(v => v.CheckedInByStaffId)
            .HasColumnName("checked_in_by_staff_id");

        builder.Property(v => v.ReasonForVisit)
            .HasColumnName("reason_for_visit");

        builder.Property(v => v.Status)
            .HasColumnName("status")
            .HasMaxLength(30);

        builder.Property(v => v.CheckedInAt)
            .HasColumnName("checked_in_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(v => v.LastPresentDate)
            .HasColumnName("last_present_date")
            .HasColumnType("date");

        builder.Property(v => v.PresenceState)
            .HasColumnName("presence_state")
            .HasMaxLength(16)
            .HasDefaultValue("PRESENT")
            .IsRequired();

        builder.Property(v => v.PresenceSessionNo)
            .HasColumnName("presence_session_no")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(v => v.LastResumedAt)
            .HasColumnName("last_resumed_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(v => v.ReadyForReturnAt)
            .HasColumnName("ready_for_return_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(v => v.OverdueReturnApprovedByDoctorId)
            .HasColumnName("overdue_return_approved_by_doctor_id");

        builder.Property(v => v.OverdueReturnApprovedAt)
            .HasColumnName("overdue_return_approved_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(v => v.OverdueReturnApprovalNote)
            .HasColumnName("overdue_return_approval_note");

        builder.Property(v => v.OverdueReturnResultSnapshot)
            .HasColumnName("overdue_return_result_snapshot")
            .HasMaxLength(128);

        builder.HasIndex(v => v.OverdueReturnApprovedByDoctorId)
            .HasDatabaseName("ix_visits_overdue_return_approved_by_doctor_id");

        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(v => v.OverdueReturnApprovedByDoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(v => v.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(v => v.CompletedByDoctorId)
            .HasColumnName("completed_by_doctor_id");
    }
}
