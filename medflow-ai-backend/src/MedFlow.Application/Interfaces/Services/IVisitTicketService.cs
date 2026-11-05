using MedFlow.Application.DTOs.Visits;

namespace MedFlow.Application.Interfaces.Services;

public interface IVisitTicketService
{
    Task<ExaminationTicketDto> GetExaminationTicketAsync(
        long actorUserId,
        string idOrVisitCode,
        CancellationToken ct = default);

    Task<ExaminationTicketDto> PrintExaminationTicketAsync(
        long actorUserId,
        string idOrVisitCode,
        PrintExaminationTicketRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);
}
