namespace MedFlow.Application.DTOs.ExaminationSlots;

public sealed record ExaminationSlotAdminItemDto(
    long Id,
    string Code,
    long? DoctorId,
    string DoctorName,
    long? ServiceId,
    string ServiceName,
    string ServiceCode,
    string ServiceType,
    string Specialty,
    long? RoomId,
    string Room,
    int CapacityBooked,
    int CapacityMax,
    string Date,
    string DateDisplay,
    string StartTime,
    string EndTime,
    string Status,
    string? Facility = null,
    bool NeedsReview = false,
    string? ClosedAt = null,
    string? CloseReason = null,
    string? CloseReasonCode = null,
    string? CloseNote = null,
    string? AffectedBookingsAction = null
);

