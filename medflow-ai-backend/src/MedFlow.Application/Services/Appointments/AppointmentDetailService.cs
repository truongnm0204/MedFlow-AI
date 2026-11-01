using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Appointments;

public sealed class AppointmentDetailService : IAppointmentDetailService
{
    private const int MaxIdentifierLength = 40;

    private const string ActionCancel = "CANCEL";
    private const string ActionReschedule = "RESCHEDULE";
    private const string ActionUpdateReason = "UPDATE_REASON";
    private const string ActionPrintSlip = "PRINT_SLIP";
    private const string ActionTrackQueue = "TRACK_QUEUE";
    private const string ActionRebook = "REBOOK";

    private readonly IApplicationDbContext _db;
    private readonly IAppointmentPolicyService _policyService;
    private readonly TimeProvider _timeProvider;

    public AppointmentDetailService(
        IApplicationDbContext db,
        IAppointmentPolicyService policyService,
        TimeProvider timeProvider)
    {
        _db = db;
        _policyService = policyService;
        _timeProvider = timeProvider;
    }

    public async Task<AppointmentDetailDto> GetMyAppointmentDetailAsync(
        long userId,
        string? idOrCode,
        CancellationToken ct = default)
    {
        if (userId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        var (trimmedIdentifier, appointmentId, appointmentCode) = ParseIdentifier(idOrCode);

        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var owned = await AppointmentBookingRules.LoadOwnedPatientsAsync(_db, userId, nowLocal, ct);
        var scope = AppointmentReadModel.BuildScopeMembers(owned.Self, owned.FamilyLinks, today);

        var query = _db.Appointments
            .AsNoTracking()
            .IncludeDisplayGraph();

        var appointment = appointmentId.HasValue
            ? await query.FirstOrDefaultAsync(a => a.Id == appointmentId.Value, ct)
            : await query.FirstOrDefaultAsync(
                a => a.AppointmentCode != null && a.AppointmentCode.ToUpper() == appointmentCode,
                ct);

        if (appointment is null)
            throw new NotFoundException("Appointment", trimmedIdentifier);

        var scopeMember = appointment.PatientId.HasValue
            ? scope.FirstOrDefault(m => m.Patient.Id == appointment.PatientId.Value)
            : null;

        if (scopeMember is null)
            throw new ForbiddenException("Bạn không có quyền xem chi tiết lịch hẹn này.");

        if (appointment.Slot is null || appointment.Slot.StartsAt is null || appointment.Slot.EndsAt is null)
            throw new NotFoundException("Appointment", trimmedIdentifier);

        var related = await AppointmentReadModel.LoadRelatedAsync(_db, [appointment.Id], ct);
        related.VisitsByAppointmentId.TryGetValue(appointment.Id, out var visit);
        related.QueueByAppointmentId.TryGetValue(appointment.Id, out var queue);
        Payment? paid = null;
        if (visit is not null)
            related.PaidByVisitId.TryGetValue(visit.Id, out paid);

        var item = AppointmentReadModel.MapItem(
            appointment,
            scopeMember,
            visit,
            queue,
            paid,
            nowLocal,
            today);

        var queueNumber = item.QueueNumber is null && item.Status is "BOOKED" or "CONFIRMED" or "PENDING"
            ? await AppointmentBookingRules.ComputeQueueNumberAsync(_db, appointment.Id, appointment.Slot, ct)
            : item.QueueNumber;

        var availableActions = await BuildAvailableActionsAsync(
            appointment.Slot,
            item.Status,
            nowLocal,
            ct);

        var (birthYearLabel, birthDateLabel, phone, address) = BuildPatientPhiFields(scopeMember.Patient, today);

        return ToDetail(
            item,
            queueNumber,
            birthYearLabel,
            birthDateLabel,
            phone,
            address,
            availableActions);
    }

    private static (string Trimmed, long? Id, string? Code) ParseIdentifier(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
            throw new ValidationException("Mã lịch hẹn không hợp lệ.");

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
            trimmed = trimmed[1..].TrimStart();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxIdentifierLength)
            throw new ValidationException("Mã lịch hẹn không hợp lệ.");

        if (trimmed.All(char.IsAsciiDigit))
        {
            if (!long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                throw new ValidationException("Mã lịch hẹn không hợp lệ.");

            return (trimmed, id, null);
        }

        return (trimmed, null, trimmed.ToUpperInvariant());
    }

    private async Task<IReadOnlyList<string>> BuildAvailableActionsAsync(
        ExaminationSlot slot,
        string status,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var actions = new List<string>(6);
        var start = slot.StartsAt!.Value;
        var modifiable = status is "BOOKED" or "CONFIRMED" or "PENDING" && start > nowLocal;

        if (modifiable)
        {
            var hospitalId = slot.Service?.HospitalId ?? slot.Doctor?.HospitalId ?? 0L;
            var policy = await _policyService.GetEffectiveAsync(hospitalId, ct);

            if (start >= nowLocal.AddHours(policy.CancelMinimumHours))
                actions.Add(ActionCancel);

            if (start >= nowLocal.AddHours(policy.RescheduleMinimumHours))
                actions.Add(ActionReschedule);

            actions.Add(ActionUpdateReason);
        }

        if (!AppointmentReadModel.IsCancelledStatus(status))
            actions.Add(ActionPrintSlip);

        if (AppointmentReadModel.IsUpcomingStatus(status))
            actions.Add(ActionTrackQueue);

        if (status == "COMPLETED" || AppointmentReadModel.IsCancelledStatus(status))
            actions.Add(ActionRebook);

        return actions;
    }

    private static (string? BirthYearLabel, string? BirthDateLabel, string? Phone, string? Address) BuildPatientPhiFields(
        Patient patient,
        DateOnly today)
    {
        string? birthYearLabel = null;
        string? birthDateLabel = null;

        if (patient.DateOfBirth is { } dob)
        {
            var vi = CultureInfo.InvariantCulture;
            var age = AppointmentSlotService.CalculateAge(dob, today).ToString(vi);
            birthYearLabel = $"{dob.Year.ToString(vi)} ({age} tuổi)";

            var datePart = $"{dob.ToString("dd/MM/yyyy", vi)} ({age} tuổi)";
            if (!string.IsNullOrWhiteSpace(patient.Gender))
            {
                var genderSuffix = patient.Gender.Trim().ToUpperInvariant() switch
                {
                    "MALE" => " • Nam",
                    "FEMALE" => " • Nữ",
                    _ => " • Khác"
                };
                datePart += genderSuffix;
            }

            birthDateLabel = datePart;
        }

        var phone = string.IsNullOrWhiteSpace(patient.Phone) ? null : patient.Phone.Trim();
        var address = string.IsNullOrWhiteSpace(patient.Address) ? null : patient.Address.Trim();

        return (birthYearLabel, birthDateLabel, phone, address);
    }

    private static AppointmentDetailDto ToDetail(
        AppointmentListItemDto item,
        string? queueNumber,
        string? patientBirthYearLabel,
        string? patientBirthDateLabel,
        string? patientPhone,
        string? patientAddress,
        IReadOnlyList<string> availableActions)
        => new(
            Id: item.Id,
            AppointmentCode: item.AppointmentCode,
            PatientId: item.PatientId,
            PatientCode: item.PatientCode,
            PatientName: item.PatientName,
            PatientRelationshipLabel: item.PatientRelationshipLabel,
            PatientAgeLabel: item.PatientAgeLabel,
            ServiceId: item.ServiceId,
            ServiceCode: item.ServiceCode,
            ServiceName: item.ServiceName,
            ServiceFullLabel: item.ServiceFullLabel,
            SpecialtyId: item.SpecialtyId,
            SpecialtyName: item.SpecialtyName,
            DepartmentName: item.DepartmentName,
            DoctorId: item.DoctorId,
            DoctorCode: item.DoctorCode,
            DoctorName: item.DoctorName,
            DoctorTitle: item.DoctorTitle,
            DoctorRoleLabel: item.DoctorRoleLabel,
            RoomCode: item.RoomCode,
            RoomName: item.RoomName,
            RoomZoneLabel: item.RoomZoneLabel,
            ScheduledDate: item.ScheduledDate,
            ScheduledDateBadge: item.ScheduledDateBadge,
            ScheduledDateFullLabel: item.ScheduledDateFullLabel,
            ScheduledTimeRange: item.ScheduledTimeRange,
            CheckInDeadlineTime: item.CheckInDeadlineTime,
            CheckInCounterLabel: item.CheckInCounterLabel,
            QueueNumber: queueNumber,
            BookingChannel: item.BookingChannel,
            Status: item.Status,
            StatusBannerLabel: item.StatusBannerLabel,
            ListedFee: item.ListedFee,
            FeePolicyNote: item.FeePolicyNote,
            PaymentStatus: item.PaymentStatus,
            PaymentStatusLabel: item.PaymentStatusLabel,
            PaymentSubNote: item.PaymentSubNote,
            ReasonForVisit: item.ReasonForVisit,
            ClinicalSummary: item.ClinicalSummary,
            CancellationReason: item.CancellationReason,
            SlotReleaseNote: item.SlotReleaseNote,
            RebookSpecialtyLabel: item.RebookSpecialtyLabel,
            BookedAt: item.BookedAt,
            PatientBirthYearLabel: patientBirthYearLabel,
            PatientBirthDateLabel: patientBirthDateLabel,
            PatientPhone: patientPhone,
            PatientAddress: patientAddress,
            AvailableActions: availableActions);
}
