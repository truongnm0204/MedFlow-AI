namespace MedFlow.Application.DTOs.Appointments;

public sealed record AppointmentListResponse(
    IReadOnlyList<AppointmentListItemDto> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    AppointmentTabCountsDto TabCounts,
    IReadOnlyList<AppointmentMemberCountDto> MemberCounts,
    int TotalAllMembersCount);
