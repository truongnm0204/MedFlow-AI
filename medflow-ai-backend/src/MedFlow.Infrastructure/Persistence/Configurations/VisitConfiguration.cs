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

        builder.Property(v => v.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(v => v.CompletedByDoctorId)
            .HasColumnName("completed_by_doctor_id");
    }
}
