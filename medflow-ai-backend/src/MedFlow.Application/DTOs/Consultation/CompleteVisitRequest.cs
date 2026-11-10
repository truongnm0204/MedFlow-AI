namespace MedFlow.Application.DTOs.Consultation;

public sealed record CompleteVisitRequest(
    string? ClinicalConclusion = null,
    string? DischargeInstructions = null,
    string? FollowUpInstructions = null,
    DateOnly? FollowUpDate = null,
    string? WarningSigns = null,
    string? Notes = null,
    string? CompletionNote = null,
    bool? ConfirmNoPrescription = null,
    int? ExpectedStateVersion = null,
    int? ExpectedRevisionNo = null);
