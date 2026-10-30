using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class StaffConfiguration : IEntityTypeConfiguration<Staff>
{
    public void Configure(EntityTypeBuilder<Staff> builder)
    {
        builder.ToTable("staff");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(s => s.UserId).HasColumnName("user_id");
        builder.Property(s => s.HospitalId).HasColumnName("hospital_id");
        builder.Property(s => s.DepartmentId).HasColumnName("department_id");

        builder.Property(s => s.StaffCode).HasColumnName("staff_code").HasMaxLength(40).IsRequired();
        builder.Property(s => s.FullName).HasColumnName("full_name").HasMaxLength(200).IsRequired();
        builder.Property(s => s.Status).HasColumnName("status").HasMaxLength(16);

        builder.HasIndex(s => new { s.HospitalId, s.StaffCode })
            .IsUnique()
            .HasDatabaseName("uq_staff_hospital_id_staff_code");
    }
}
