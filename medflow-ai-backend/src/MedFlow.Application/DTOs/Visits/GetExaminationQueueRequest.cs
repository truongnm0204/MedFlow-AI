namespace MedFlow.Application.DTOs.Visits;

public sealed record GetExaminationQueueRequest(
    string? QueueDate = null,
    string? ServicePointId = null,
    string? Status = null,
    string? Priority = null,
    string? VisitType = null,
    string? Keyword = null,
    string? QueueType = null);
