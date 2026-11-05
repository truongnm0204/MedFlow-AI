namespace MedFlow.Application.DTOs.ExaminationSlots;

public sealed record GetExaminationSlotListRequest(
    string? Keyword = null,
    string? Date = null,
    string? FromDate = null,
    string? ToDate = null,
    string? Specialty = null,
    long? SpecialtyId = null,
    long? DoctorId = null,
    long? ServiceId = null,
    long? RoomId = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20
);

