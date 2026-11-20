namespace MedFlow.Application.DTOs.Services;

/// <summary>
/// Yêu cầu kích hoạt lại dịch vụ khám đã ngừng hoạt động (UC50 / #196).
/// </summary>
public sealed record ReactivateExaminationServiceRequest(
    string? Reason = null,
    string? Note = null
);
