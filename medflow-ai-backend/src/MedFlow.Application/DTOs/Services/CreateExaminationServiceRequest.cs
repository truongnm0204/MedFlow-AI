namespace MedFlow.Application.DTOs.Services;

public sealed record CreateExaminationServiceRequest(
    string? ServiceCode = null,
    string? Code = null,
    string? Name = null,
    string? ServiceType = null,
    string? Category = null,
    long? DepartmentId = null,
    string? Department = null,
    long? SpecialtyId = null,
    long? HospitalId = null,
    string? Description = null,
    decimal? Price = null,
    decimal? InitialPrice = null,
    bool? AllowsOnlineBooking = null,
    bool? OnlineBooking = null,
    bool? RequiresResultVerification = null,
    string? Status = null,
    bool? IsActive = null
);

