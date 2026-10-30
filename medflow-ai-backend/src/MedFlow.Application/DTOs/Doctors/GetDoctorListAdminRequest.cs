namespace MedFlow.Application.DTOs.Doctors;

public sealed record GetDoctorListAdminRequest(
    string? Keyword = null,
    long? DepartmentId = null,
    long? SpecialtyId = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 10
);

