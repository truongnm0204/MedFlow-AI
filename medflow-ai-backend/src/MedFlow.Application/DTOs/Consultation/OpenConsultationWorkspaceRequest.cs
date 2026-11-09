namespace MedFlow.Application.DTOs.Consultation;

public sealed record OpenConsultationWorkspaceRequest(
    int? ExpectedStateVersion = null,
    string? Notes = null);
