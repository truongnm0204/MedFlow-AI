using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Entities;

namespace MedFlow.Application.Services.Appointments;

public sealed class AppointmentDetailService : IAppointmentDetailService
{
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
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var view = await AppointmentReadModel.LoadOwnedAppointmentViewAsync(
            _db,
            userId,
            idOrCode,
            nowLocal,
            "Bạn không có quyền xem chi tiết lịch hẹn này.",
            ct);

        var item = AppointmentReadModel.MapItem(
            view.Appointment,
            view.ScopeMember,
            view.Visit,
            view.Queue,
            view.Paid,
            nowLocal,
            today);

        var queueNumber = await AppointmentReadModel.ResolveDisplayQueueNumberAsync(
            _db,
            item,
            view.Appointment,
            ct);

        var availableActions = await BuildAvailableActionsAsync(
            view.Appointment.Slot!,
            item.Status,
            nowLocal,
            ct);

        var (birthYearLabel, birthDateLabel, phone, address) = BuildPatientPhiFields(view.ScopeMember.Patient, today);

        return ToDetail(
            item,
            queueNumber,
            birthYearLabel,
            birthDateLabel,
            phone,
            address,
            availableActions);
    }

    private async Task<IReadOnlyList<string>> BuildAvailableActionsAsync(
        ExaminationSlot slot,
        string status,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var actions = new List<string>(6);
        var start = slot.StartsAt!.Value;
        var modifiable = AppointmentReadModel.IsModifiableStatus(status) && start > nowLocal;

        if (modifiable)
        {
            var hospitalId = slot.Service?.HospitalId ?? slot.Doctor?.HospitalId ?? 0L;
            var policy = await _policyService.GetEffectiveAsync(hospitalId, ct);

            if (AppointmentReadModel.IsWithinCancelWindow(start, nowLocal, policy.CancelMinimumHours))
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
