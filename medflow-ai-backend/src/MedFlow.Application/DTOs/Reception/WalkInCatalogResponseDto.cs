namespace MedFlow.Application.DTOs.Reception;

public sealed record WalkInServiceCatalogItemDto(
    long Id,
    string Code,
    string Name,
    decimal Price,
    string PriceFormatted);

public sealed record WalkInDepartmentCatalogItemDto(
    long Id,
    string Code,
    string Name,
    string Location,
    IReadOnlyList<WalkInServiceCatalogItemDto> Services);

public sealed record WalkInCatalogResponseDto(
    long HospitalId,
    string HospitalName,
    IReadOnlyList<WalkInDepartmentCatalogItemDto> Departments,
    IReadOnlyList<string> SymptomOptions);
