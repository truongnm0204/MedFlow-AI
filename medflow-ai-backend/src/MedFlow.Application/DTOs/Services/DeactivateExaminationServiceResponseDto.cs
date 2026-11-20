namespace MedFlow.Application.DTOs.Services;

/// <summary>
/// Kết quả phản hồi sau khi ngừng hoạt động hoặc kích hoạt lại dịch vụ khám (UC50 / #196).
/// </summary>
public sealed record DeactivateExaminationServiceResponseDto(
    long Id,
    string ServiceCode,
    string Name,
    bool IsActive,
    string Status,
    string Message,
    int ActiveBookingsCount,
    int ActiveSlotsCount,
    DateTime OccurredAtUtc
);
