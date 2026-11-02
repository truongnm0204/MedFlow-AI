namespace MedFlow.Application.DTOs.Services;

public sealed record ExaminationServiceAdminItemDto(
    long Id,
    string ServiceCode,
    string Name,
    string? Description,
    string ServiceType,
    string Category,
    long? HospitalId,
    string Facility,
    long? DepartmentId,
    string? DepartmentCode,
    string? DepartmentName,
    long? SpecialtyId,
    string? SpecialtyCode,
    string? SpecialtyName,
    decimal Price,
    string PriceFormatted,
    bool AllowsOnlineBooking,
    string BookingChannel,
    bool RequiresResultVerification,
    bool IsActive,
    string Status
);
