namespace MedFlow.Application.DTOs.HospitalAdmin;

public sealed record HospitalPolicyAlertItemDto(
    string Id,
    string Code,
    string Title,
    string Detail,
    string Severity,
    string Route
);
