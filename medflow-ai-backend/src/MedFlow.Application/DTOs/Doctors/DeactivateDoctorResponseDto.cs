namespace MedFlow.Application.DTOs.Doctors;

public sealed record DeactivateDoctorResponseDto(
    long DoctorId,
    string DoctorCode,
    string FullName,
    bool IsActive,
    string Status,
    string Message,
    bool HasActiveBookings,
    int ActiveBookingsCount,
    int ActiveSlotsCount,
    DateTime OccurredAtUtc
);
