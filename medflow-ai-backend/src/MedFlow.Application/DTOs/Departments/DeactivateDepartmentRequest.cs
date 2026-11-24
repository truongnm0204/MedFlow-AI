namespace MedFlow.Application.DTOs.Departments;

public sealed record DeactivateDepartmentRequest(
    string? Reason = null,
    string? Note = null,
    bool ConfirmUnderstood = false
);
