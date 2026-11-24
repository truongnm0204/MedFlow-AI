namespace MedFlow.Application.DTOs.Departments;

public sealed record ReactivateDepartmentRequest(
    string? Reason = null
);
