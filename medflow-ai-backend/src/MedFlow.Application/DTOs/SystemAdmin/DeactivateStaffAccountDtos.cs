namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record DeactivateStaffAccountRequest(string? Reason = null);

public sealed record DeactivateStaffAccountResponseDto(
    long StaffId,
    long UserId,
    string StaffCode,
    string AccountStatus,
    string StaffStatus,
    bool DoctorProfileDeactivated,
    int RevokedSessionCount,
    DateTime DeactivatedAtUtc);
