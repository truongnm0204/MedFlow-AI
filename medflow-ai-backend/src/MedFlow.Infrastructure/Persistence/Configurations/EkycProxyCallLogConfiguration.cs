using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedFlow.Infrastructure.Persistence.Configurations;

public class EkycProxyCallLogConfiguration : IEntityTypeConfiguration<EkycProxyCallLog>
{
    public void Configure(EntityTypeBuilder<EkycProxyCallLog> builder)
    {
        builder.ToTable("ekyc_proxy_call_logs", table =>
        {
            table.HasCheckConstraint(
                "chk_ekyc_log_http_status",
                "\"http_status\" >= 100 AND \"http_status\" <= 599");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(x => x.EndpointPath).HasColumnName("endpoint_path").HasMaxLength(256).IsRequired();
        builder.Property(x => x.HttpMethod).HasColumnName("http_method").HasMaxLength(8).IsRequired();
        builder.Property(x => x.HttpStatus).HasColumnName("http_status").IsRequired();
        builder.Property(x => x.RequestObjectHashes).HasColumnName("request_object_hashes").HasColumnType("jsonb");
        builder.Property(x => x.ResponseObjectHash).HasColumnName("response_object_hash").HasMaxLength(256);
        builder.Property(x => x.ResponseDataSign).HasColumnName("response_data_sign");
        builder.Property(x => x.ResponsePayloadEncrypted).HasColumnName("response_payload_encrypted");
        builder.Property(x => x.ParseWarning).HasColumnName("parse_warning").HasMaxLength(128);
        builder.Property(x => x.DurationMs).HasColumnName("duration_ms");
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        builder.HasIndex(x => new { x.SessionId, x.CreatedAt }).HasDatabaseName("ix_ekyc_logs_session_created");
        builder.HasIndex(x => x.ResponseObjectHash)
            .HasDatabaseName("ix_ekyc_logs_response_object_hash")
            .HasFilter("\"response_object_hash\" IS NOT NULL");
    }
}
