namespace MedFlow.Application.DTOs.Services;

public sealed record GetServicePriceListAdminRequest(
    int Page = 1,
    int PageSize = 50,
    string? Keyword = null,
    long? DepartmentId = null,
    string? Category = null,
    string? ServiceType = null,
    string? Status = null,
    bool? IsActive = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    long? HospitalId = null);

public sealed record ServicePriceHistoryEntryDto(
    long Id,
    decimal Price,
    decimal PriceAmount,
    string PriceFormatted,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    string Date,
    string? DecisionNumber,
    string? Note,
    string? AppliedBy,
    bool IsActive,
    string Status);

public sealed record ServicePricingMetricsDto(
    int TotalServices,
    int PricedServicesCount,
    int PricedRatioPercent,
    decimal AveragePrice,
    decimal MinPrice,
    decimal MaxPrice,
    string? LatestDecisionNumber,
    string? LatestEffectiveDate);

public sealed record ServicePriceAdminItemDto(
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
    decimal Price,
    decimal? CurrentPriceAmount,
    bool HasEffectivePrice,
    string PriceFormatted,
    bool AllowsOnlineBooking,
    bool IsActive,
    string Status,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    string? EffectiveDate,
    string? DecisionNumber,
    string? AppliedBy,
    string? Note,
    IReadOnlyList<ServicePriceHistoryEntryDto> PriceHistory);

public sealed record ServicePriceAdminListResponseDto(
    long? HospitalId,
    string? HospitalName,
    IReadOnlyList<ServicePriceAdminItemDto> Data,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    ServicePricingMetricsDto Metrics);

public sealed record ServicePriceDetailResponseDto(
    long ServiceId,
    string ServiceCode,
    string Name,
    string ServiceType,
    string Category,
    long? HospitalId,
    string Facility,
    long? DepartmentId,
    string? DepartmentName,
    bool IsActive,
    string Status,
    decimal Price,
    decimal? CurrentPriceAmount,
    bool HasEffectivePrice,
    string PriceFormatted,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    string? EffectiveDate,
    string? DecisionNumber,
    string? AppliedBy,
    string? Note,
    IReadOnlyList<ServicePriceHistoryEntryDto> PriceHistory);

public sealed record UpdateServicePriceAdminRequest(
    long? ServiceId = null,
    string? ServiceCode = null,
    long? HospitalId = null,
    decimal? PriceAmount = null,
    decimal? Price = null,
    string? ValidFrom = null,
    string? EffectiveDate = null,
    string? ValidTo = null,
    string? DecisionNumber = null,
    string? AppliedBy = null,
    string? Note = null,
    string? Reason = null);
