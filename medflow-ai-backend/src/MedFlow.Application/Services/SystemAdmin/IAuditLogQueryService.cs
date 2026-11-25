using MedFlow.Application.DTOs.SystemAdmin;

namespace MedFlow.Application.Services.SystemAdmin;

public interface IAuditLogQueryService
{
    Task<AuditLogListResponseDto> GetAuditLogsAsync(
        long actorUserId,
        GetAuditLogsRequest request,
        CancellationToken ct = default);

    Task<AuditLogDetailDto> GetAuditLogByIdAsync(
        long actorUserId,
        long auditLogId,
        CancellationToken ct = default);
}
