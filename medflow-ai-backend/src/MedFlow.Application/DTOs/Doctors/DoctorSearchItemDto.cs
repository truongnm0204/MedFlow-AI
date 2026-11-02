namespace MedFlow.Application.DTOs.Doctors;

public sealed record DoctorSearchItemDto(
    string Id,
    string Name,
    string Title,
    string RoleTag,
    string Specialty,
    string SpecialtyId,
    int YearsOfExperience,
    string Room,
    string NearestSchedule,
    string ScheduleStatus,
    string ScheduleStatusText,
    decimal Price,
    string AvatarUrl,
    bool HasScheduleToday,
    bool HasScheduleThisWeek);
