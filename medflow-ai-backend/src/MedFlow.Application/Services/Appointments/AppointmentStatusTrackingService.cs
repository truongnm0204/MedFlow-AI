using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.Interfaces.Persistence;

namespace MedFlow.Application.Services.Appointments;

public sealed class AppointmentStatusTrackingService : IAppointmentStatusTrackingService
{
    private const string StepBooked = "BOOKED";
    private const string StepCheckedIn = "CHECKED_IN";
    private const string StepInConsultation = "IN_CONSULTATION";
    private const string StepCompleted = "COMPLETED";
    private const string StepCancelled = "CANCELLED";
    private const string StepNoShow = "NO_SHOW";

    private const string StateDone = "done";
    private const string StateCurrent = "current";
    private const string StateUpcoming = "upcoming";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public AppointmentStatusTrackingService(
        IApplicationDbContext db,
        TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<AppointmentStatusTrackingDto> GetMyAppointmentStatusAsync(
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
            "Bạn không có quyền theo dõi trạng thái lịch hẹn này.",
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

        var steps = BuildSteps(item.Status, view);
        var nextStep = BuildNextStep(item.Status, view, item, queueNumber, nowLocal);

        return new AppointmentStatusTrackingDto(
            AppointmentId: item.Id,
            AppointmentCode: item.AppointmentCode,
            PatientName: item.PatientName,
            PatientRelationshipLabel: item.PatientRelationshipLabel,
            Status: item.Status,
            StatusLabel: item.StatusBannerLabel,
            ScheduledDateFullLabel: item.ScheduledDateFullLabel,
            ScheduledTimeRange: item.ScheduledTimeRange,
            DoctorName: item.DoctorName,
            DepartmentName: item.DepartmentName,
            RoomName: item.RoomName,
            QueueNumber: queueNumber,
            Steps: steps,
            NextStep: nextStep);
    }

    private static IReadOnlyList<AppointmentStatusStepDto> BuildSteps(
        string status,
        AppointmentReadModel.OwnedAppointmentView view)
    {
        var bookedAt = view.Appointment.BookedAt;

        if (status == "CANCELLED")
        {
            return
            [
                Step(StepBooked, "Đặt lịch thành công", bookedAt),
                Step(StepCancelled, "Đã hủy lịch", view.Appointment.CancelledAt),
            ];
        }

        if (status == "NO_SHOW")
        {
            return
            [
                Step(StepBooked, "Đặt lịch thành công", bookedAt),
                Step(StepNoShow, "Vắng mặt", null),
            ];
        }

        var checkedInAt = view.Visit?.CheckedInAt;
        var startedAt = view.Queue?.StartedAt;
        var completedAt = view.Visit?.CompletedAt;

        var reachedCheckedIn = status is "CHECKED_IN" or "COMPLETED";
        var reachedInConsultation = status == "COMPLETED" || (status == "CHECKED_IN" && startedAt != null);
        var reachedCompleted = status == "COMPLETED";

        var reached = new[] { true, reachedCheckedIn, reachedInConsultation, reachedCompleted };
        var firstUnreached = Array.IndexOf(reached, false);

        string StateFor(int index)
            => reached[index]
                ? StateDone
                : index == firstUnreached
                    ? StateCurrent
                    : StateUpcoming;

        return
        [
            new AppointmentStatusStepDto(StepBooked, "Đặt lịch thành công", StateFor(0), FormatOrNull(bookedAt)),
            new AppointmentStatusStepDto(StepCheckedIn, "Check-in tại quầy tiếp đón", StateFor(1), reachedCheckedIn ? FormatOrNull(checkedInAt) : null),
            new AppointmentStatusStepDto(StepInConsultation, "Vào phòng khám", StateFor(2), reachedInConsultation ? FormatOrNull(startedAt) : null),
            new AppointmentStatusStepDto(StepCompleted, "Hoàn tất khám", StateFor(3), reachedCompleted ? FormatOrNull(completedAt) : null),
        ];

        static AppointmentStatusStepDto Step(string key, string title, DateTime? occurredAt)
            => new(key, title, StateDone, FormatOrNull(occurredAt));
    }

    private static AppointmentNextStepDto BuildNextStep(
        string status,
        AppointmentReadModel.OwnedAppointmentView view,
        AppointmentListItemDto item,
        string? queueNumber,
        DateTime nowLocal)
    {
        var roomLabel = string.IsNullOrWhiteSpace(item.RoomName) ? "phòng khám" : item.RoomName;
        var start = view.Appointment.Slot!.StartsAt!.Value;
        var startedAt = view.Queue?.StartedAt;
        var calledAt = view.Queue?.CalledAt;

        switch (status)
        {
            case "CANCELLED":
                return new AppointmentNextStepDto(
                    "REBOOK",
                    "Đặt lịch mới",
                    "Lịch hẹn đã được hủy. Bạn có thể đặt lịch khám mới khi cần.",
                    null);

            case "NO_SHOW":
                return new AppointmentNextStepDto(
                    "REBOOK",
                    "Đặt lịch mới",
                    "Bạn đã vắng mặt ở lịch hẹn này. Vui lòng đặt lịch khám mới nếu vẫn cần thăm khám.",
                    null);

            case "COMPLETED":
                return new AppointmentNextStepDto(
                    "FOLLOW_UP",
                    "Theo dõi sau khám",
                    "Lượt khám đã hoàn tất. Vui lòng làm theo chỉ định của bác sĩ và đặt lịch tái khám nếu được hẹn.",
                    null);

            case "PENDING":
                return new AppointmentNextStepDto(
                    "WAIT_FOR_CONFIRMATION",
                    "Chờ bệnh viện xác nhận",
                    "Lịch hẹn đang chờ bệnh viện xác nhận. Vui lòng theo dõi trạng thái trước ngày khám.",
                    null);

            case "BOOKED":
            case "CONFIRMED":
                if (start >= nowLocal)
                {
                    var deadline = start.AddMinutes(-15);
                    return new AppointmentNextStepDto(
                        "CHECK_IN",
                        "Check-in tại quầy tiếp đón",
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Vui lòng có mặt và check-in trước {0:HH:mm} ngày {1:dd/MM/yyyy}.",
                            deadline,
                            start),
                        AppointmentReadModel.FormatVietnamTimestamp(deadline));
                }

                return new AppointmentNextStepDto(
                    "CONTACT_RECEPTION",
                    "Liên hệ quầy tiếp đón",
                    "Đã quá giờ hẹn nhưng lịch hẹn chưa được check-in. Vui lòng liên hệ quầy tiếp đón để được hỗ trợ.",
                    null);

            case "CHECKED_IN":
                if (startedAt != null)
                {
                    return new AppointmentNextStepDto(
                        "IN_CONSULTATION",
                        "Đang khám",
                        "Bạn đang trong lượt khám. Vui lòng làm theo hướng dẫn của bác sĩ.",
                        null);
                }

                if (calledAt != null)
                {
                    return new AppointmentNextStepDto(
                        "GO_TO_ROOM",
                        "Vào phòng khám",
                        string.Format(CultureInfo.InvariantCulture, "Đã đến lượt của bạn. Vui lòng vào {0}.", roomLabel),
                        null);
                }

                return new AppointmentNextStepDto(
                    "WAIT_FOR_CALL",
                    "Chờ gọi số",
                    queueNumber != null
                        ? string.Format(
                            CultureInfo.InvariantCulture,
                            "Vui lòng chờ tại khu vực {0} đến khi được gọi số {1}.",
                            roomLabel,
                            queueNumber)
                        : string.Format(
                            CultureInfo.InvariantCulture,
                            "Vui lòng chờ tại khu vực {0} đến khi được gọi tên.",
                            roomLabel),
                    null);

            default:
                throw new InvalidOperationException($"Trạng thái lịch hẹn không hỗ trợ theo dõi: {status}.");
        }
    }

    private static string? FormatOrNull(DateTime? value)
        => value is { } v ? AppointmentReadModel.FormatVietnamTimestamp(v) : null;
}
