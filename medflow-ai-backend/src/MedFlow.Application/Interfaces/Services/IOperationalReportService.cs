using MedFlow.Application.DTOs.Reports;

namespace MedFlow.Application.Interfaces.Services;

public interface IOperationalReportService
{
    Task<OperationalReportResponseDto> GetOperationalReportAsync(
        long actorUserId,
        GetOperationalReportRequest request,
        CancellationToken ct = default);
}
