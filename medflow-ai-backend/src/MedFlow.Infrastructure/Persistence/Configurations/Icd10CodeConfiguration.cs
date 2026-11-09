using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class Icd10CodeConfiguration : IEntityTypeConfiguration<Icd10Code>
{
    public void Configure(EntityTypeBuilder<Icd10Code> builder)
    {
        builder.ToTable("icd10_codes");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(x => x.ParentId)
            .HasColumnName("parent_id");

        builder.Property(x => x.Code)
            .HasColumnName("code")
            .HasMaxLength(20);

        builder.Property(x => x.NameVi)
            .HasColumnName("name_vi");

        builder.Property(x => x.CatalogVersion)
            .HasColumnName("catalog_version")
            .HasMaxLength(30);

        builder.Property(x => x.NodeType)
            .HasColumnName("node_type")
            .HasMaxLength(20);

        builder.Property(x => x.IsSelectable)
            .HasColumnName("is_selectable");

        builder.Property(x => x.NotPrimaryDx)
            .HasColumnName("not_primary_dx");
    }
}
