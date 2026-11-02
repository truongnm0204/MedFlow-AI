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

        builder.Property(v => v.AppointmentId)
            .HasColumnName("appointment_id");

        builder.Property(v => v.ReasonForVisit)
            .HasColumnName("reason_for_visit");

        builder.Property(v => v.Status)
            .HasColumnName("status")
            .HasMaxLength(30);

        builder.Property(v => v.CheckedInAt)
            .HasColumnName("checked_in_at");

        builder.Property(v => v.CompletedAt)
            .HasColumnName("completed_at");
    }
}
