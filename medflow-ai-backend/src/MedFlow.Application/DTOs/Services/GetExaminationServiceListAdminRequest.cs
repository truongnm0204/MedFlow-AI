namespace MedFlow.Application.DTOs.Services;

public sealed record GetExaminationServiceListAdminRequest(
    string? Keyword = null,
    long? DepartmentId = null,
    long? SpecialtyId = null,
    long? HospitalId = null,
    string? ServiceType = null,
    string? Category = null,
    string? BookingChannel = null,
    bool? AllowsOnlineBooking = null,
    string? Status = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 10
);
