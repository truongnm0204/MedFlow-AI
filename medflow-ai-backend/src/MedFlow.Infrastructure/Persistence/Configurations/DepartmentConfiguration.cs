using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("departments");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(d => d.HospitalId)
            .HasColumnName("hospital_id");

        builder.Property(d => d.DepartmentCode)
            .HasColumnName("department_code")
            .HasMaxLength(40);

        builder.Property(d => d.Name)
            .HasColumnName("name")
            .HasMaxLength(200);

        builder.Property(d => d.IsActive)
            .HasColumnName("is_active");

        builder.HasIndex(d => new { d.HospitalId, d.DepartmentCode })
            .IsUnique()
            .HasDatabaseName("uq_departments_hospital_id_department_code");
    }
}
