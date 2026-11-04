namespace MedFlow.Application.DTOs.Visits;

public sealed record PrintExaminationTicketRequest(
    int? Copies = null,
    bool? IsReprint = null,
    string? Reason = null,
    string? Note = null
);
