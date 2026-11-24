namespace MedFlow.Application.DTOs.Reports;

public sealed record GetOperationalReportRequest(
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    long? DepartmentId = null,
    long? ServiceId = null,
    long? HospitalId = null);

public sealed record OperationalReportDepartmentOptionDto(
    long Id,
    string Code,
    string Name);

public sealed record OperationalReportSummaryDto(
    int TotalVisits,
    int TotalAppointments,
    int CompletedVisits,
    int InProgressVisits,
    int WaitingVisits,
    int WalkInVisits,
    int NoShowAppointments,
    double CompletionRatePercent,
    double? AverageWaitMinutes,
    double? MedianWaitMinutes);

public sealed record OperationalDailyVisitPointDto(
    DateOnly Date,
    string DateLabel,
    int TotalVisits,
    int CompletedVisits,
    int WalkInVisits);

public sealed record OperationalHourlyIntakePointDto(
    string Hour,
    int Count,
    bool IsPeak);

public sealed record OperationalDepartmentVisitItemDto(
    long? DepartmentId,
    string DepartmentCode,
    string DepartmentName,
    int Count);

public sealed record OperationalVisitStatusSegmentDto(
    string Key,
    string Label,
    int Count,
    double PercentValue);

public sealed record OperationalDepartmentRowDto(
    string Id,
    string Code,
    string Name,
    long? DepartmentId,
    string DepartmentName,
    string FacilityName,
    string PeriodLabel,
    int TotalVisits,
    int CompletedVisits,
    int WalkInVisits,
    int TotalAppointments,
    int NoShowAppointments,
    double? AverageWaitMinutes,
    double? MedianWaitMinutes,
    string Status);

public sealed record OperationalReportResponseDto(
    long? HospitalId,
    string FacilityCode,
    string FacilityName,
    DateOnly FromDate,
    DateOnly ToDate,
    string PeriodLabel,
    long? SelectedDepartmentId,
    long? SelectedServiceId,
    DateTime GeneratedAtUtc,
    OperationalReportSummaryDto Summary,
    IReadOnlyList<OperationalDailyVisitPointDto> DailyVisits,
    IReadOnlyList<OperationalHourlyIntakePointDto> HourlyIntake,
    IReadOnlyList<OperationalDepartmentVisitItemDto> DepartmentVisits,
    IReadOnlyList<OperationalVisitStatusSegmentDto> VisitStatusSegments,
    IReadOnlyList<OperationalDepartmentRowDto> DepartmentRows,
    IReadOnlyList<OperationalReportDepartmentOptionDto> AvailableDepartments);
