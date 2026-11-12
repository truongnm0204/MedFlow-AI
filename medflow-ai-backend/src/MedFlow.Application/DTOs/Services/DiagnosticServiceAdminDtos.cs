namespace MedFlow.Application.DTOs.Services;

public sealed record GetDiagnosticServiceListAdminRequest(
    string? Keyword = null,
    long? DepartmentId = null,
    long? SpecialtyId = null,
    string? Status = null,                 // ACTIVE | INACTIVE | ALL | null
    bool? RequiresResultVerification = null,
    int Page = 1,
    int PageSize = 10);

public sealed record DiagnosticServiceAdminServicePointDto(
    long ServicePointId,
    string Name,
    long? RoomId,
    string? RoomCode,
    string? RoomName,
    bool IsActive);

public sealed record DiagnosticServiceAdminItemDto(
    long Id,
    string ServiceCode,
    string Name,
    string? Description,
    string ServiceType,
    long? DepartmentId,
    string? DepartmentCode,
    string? DepartmentName,
    long? SpecialtyId,
    string? SpecialtyCode,
    string? SpecialtyName,
    decimal? Price,
    string? PriceFormatted,
    bool RequiresResultVerification,
    bool IsActive,
    string Status,                         // "ACTIVE" | "INACTIVE"
    int ServicePointCount,
    int ActiveServicePointCount,
    IReadOnlyList<DiagnosticServiceAdminServicePointDto> ServicePoints);

public sealed record DiagnosticServiceAdminMetricsDto(
    int Total,
    int Active,
    int Inactive,
    int RequiresVerificationCount,
    int DepartmentsCount,
    int ActiveServicePointsCount);

public sealed record DiagnosticServiceAdminListResponseDto(
    long HospitalId,
    string HospitalCode,
    string HospitalName,
    IReadOnlyList<DiagnosticServiceAdminItemDto> Data,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    DiagnosticServiceAdminMetricsDto Metrics);
