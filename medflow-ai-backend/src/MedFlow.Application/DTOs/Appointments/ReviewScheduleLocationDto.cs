namespace MedFlow.Application.DTOs.Appointments;

public sealed record ReviewScheduleLocationDto(
    string Mode,
    string DoctorId,
    string DoctorName,
    string DoctorRoleBadge,
    string DoctorCredentialsSubtitle,
    string DoctorAvatarUrl,
    string SpecialtyName,
    string ServiceId,
    string ServiceCode,
    string ServiceName,
    string ServiceDisplayLabel,
    string DateIso,
    string SlotOrShiftId,
    string ScheduledSlotDisplay,
    string CheckInDeadlineTime,
    string AllocatedRoomTitle,
    string ShortRoomNumber,
    string DurationBadgeLabel,
    string DurationFootnote);
