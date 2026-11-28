namespace MedFlow.Application.DTOs.Pharmacy;

public sealed record GetMedicinePriceListAdminRequest(
    int Page = 1,
    int PageSize = 50,
    string? Keyword = null,
    long? MedicineId = null,
    long? HospitalId = null,
    bool? IsActive = null,
    bool? HasEffectivePrice = null,
    string? EffectiveAt = null);

public sealed record MedicinePriceHistoryEntryDto(
    long Id,
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

public sealed record MedicinePricingMetricsDto(
    int TotalMedicines,
    int PricedMedicinesCount,
    int PricedRatioPercent,
    decimal AveragePrice,
    decimal MinPrice,
    decimal MaxPrice,
    string? LatestEffectiveDate);

public sealed record MedicinePriceAdminItemDto(
    long Id,
    string? MedicineCode,
    string Name,
    string? ActiveIngredient,
    string? Strength,
    string? DosageForm,
    string? DispensingUnit,
    long? HospitalId,
    string Facility,
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
    IReadOnlyList<MedicinePriceHistoryEntryDto> PriceHistory);

public sealed record MedicinePriceAdminListResponseDto(
    long? HospitalId,
    string? HospitalName,
    IReadOnlyList<MedicinePriceAdminItemDto> Data,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    MedicinePricingMetricsDto Metrics);

public sealed record MedicinePriceDetailResponseDto(
    long MedicineId,
    string? MedicineCode,
    string Name,
    string? ActiveIngredient,
    string? Strength,
    string? DosageForm,
    string? DispensingUnit,
    long? HospitalId,
    string Facility,
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
    IReadOnlyList<MedicinePriceHistoryEntryDto> PriceHistory);

public sealed record MedicinePriceHistoryResponseDto(
    long MedicineId,
    string? MedicineCode,
    string Name,
    long? HospitalId,
    IReadOnlyList<MedicinePriceHistoryEntryDto> PriceHistory);

public sealed record UpdateMedicinePriceAdminRequest(
    long? MedicineId = null,
    string? MedicineCode = null,
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
