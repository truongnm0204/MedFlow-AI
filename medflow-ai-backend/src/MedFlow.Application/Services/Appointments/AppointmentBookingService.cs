using System.Globalization;
using MedFlow.Application.Common.Persistence;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.Notifications;
using MedFlow.Application.Services.Policies;
using MedFlow.Application.Services.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.Application.Services.Appointments;

public sealed class AppointmentBookingService : IAppointmentBookingService
{
    private const int MaxSymptomNoteLength = 500;

    internal const string InactiveResourceConflictMessage =
        "Khung giờ khám không còn khả dụng do bác sĩ, buồng khám hoặc dịch vụ đang tạm ngưng hoạt động.";

    internal static readonly string[] ServingQueueStatuses =
    [
        "WAITING",
        "READY",
        "CALLED",
        "IN_PROGRESS",
        "IN_CONSULTATION",
        "COMPLETED",
        "SKIPPED",
    ];

    private readonly IApplicationDbContext _db;
    private readonly IDoctorCatalogService _doctorCatalog;
    private readonly IExaminationServiceCatalogService _serviceCatalog;
    private readonly IAppointmentPolicyService _policyService;
    private readonly IAppointmentBookingLock _bookingLock;
    private readonly IAuditLogger _audit;
    private readonly TimeProvider _timeProvider;
    private readonly IRealtimeNotificationPublisher _realtimePublisher;

    public AppointmentBookingService(
        IApplicationDbContext db,
        IDoctorCatalogService doctorCatalog,
        IExaminationServiceCatalogService serviceCatalog,
        IAppointmentPolicyService policyService,
        IAppointmentBookingLock bookingLock,
        IAuditLogger audit,
        TimeProvider timeProvider,
        IRealtimeNotificationPublisher? realtimePublisher = null)
    {
        _db = db;
        _doctorCatalog = doctorCatalog;
        _serviceCatalog = serviceCatalog;
        _policyService = policyService;
        _bookingLock = bookingLock;
        _audit = audit;
        _timeProvider = timeProvider;
        _realtimePublisher = realtimePublisher ?? MedFlow.Application.Services.Realtime.NullRealtimeNotificationPublisher.Instance;
    }

    public async Task<CreateAppointmentResponse> CreateAsync(
        long userId,
        CreateAppointmentRequest request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var selection = await ResolveSelectionAsync(userId, request, ct);

        await using var tx = await BeginTransactionSafeAsync(_db, ct);

        ExaminationSlot slot;
        Appointment appointment;
        string appointmentIdStr;
        string queueNumber;

        try
        {
            if (selection.IsDoctorMode)
            {
                var doctorIds = selection.Slot.DoctorId.HasValue
                    ? new[] { selection.Slot.DoctorId.Value }
                    : Array.Empty<long>();
                var roomIds = selection.Slot.RoomId.HasValue
                    ? new[] { selection.Slot.RoomId.Value }
                    : Array.Empty<long>();

                await _bookingLock.AcquireBatchAsync(
                    selection.PatientId,
                    selection.ServiceId,
                    doctorIds,
                    roomIds,
                    [selection.Slot.Id],
                    ct);

                slot = await IncludeResourceGraph(_db.ExaminationSlots.AsNoTracking())
                    .FirstOrDefaultAsync(s => s.Id == selection.Slot.Id, ct)
                    ?? throw new NotFoundException("ExaminationSlot", selection.Slot.Id);

                if (slot.DoctorId != selection.Slot.DoctorId ||
                    slot.ServiceId != selection.ServiceId ||
                    slot.StartsAt != selection.WindowStartsAt)
                {
                    throw new ConflictException("Khung giờ khám không còn khớp với bác sĩ hoặc dịch vụ đã chọn.");
                }

                AppointmentBookingRules.EnsureSlotBookable(slot, selection.NowLocal, selection.Today, selection.Policy);

                var activeSlotCheck = await FilterSlotsByActiveResourcesAsync(
                    _db,
                    [slot],
                    requireDoctorAuthorization: true,
                    ct);
                if (activeSlotCheck.Count == 0)
                {
                    throw new ConflictException(InactiveResourceConflictMessage);
                }

                await AppointmentBookingRules.EnsureSlotHasCapacityAsync(_db, slot, ct);
                await AppointmentBookingRules.EnsurePatientLimitsAsync(
                    _db,
                    selection.PatientId,
                    slot,
                    selection.NowLocal,
                    selection.Policy,
                    null,
                    ct);
            }
            else
            {
                List<ExaminationSlot> preLockCandidates;
                if (selection.HasExplicitSlotId)
                {
                    preLockCandidates = await _db.ExaminationSlots
                        .AsNoTracking()
                        .Where(s =>
                            s.ServiceId == selection.ServiceId &&
                            s.Status.ToUpper() == "OPEN" &&
                            s.StartsAt == selection.WindowStartsAt &&
                            s.EndsAt == selection.WindowEndsAt)
                        .OrderBy(s => s.Id)
                        .ToListAsync(ct);
                }
                else
                {
                    preLockCandidates = await LoadRawServiceShiftCandidatesAsync(
                        selection.ServiceId,
                        selection.DateIso,
                        selection.IsMorningShift,
                        selection.NowLocal,
                        selection.Today,
                        selection.Policy,
                        ct);
                }

                var doctorIds = preLockCandidates
                    .Where(s => s.DoctorId.HasValue)
                    .Select(s => s.DoctorId!.Value)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToArray();
                var roomIds = preLockCandidates
                    .Where(s => s.RoomId.HasValue)
                    .Select(s => s.RoomId!.Value)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToArray();
                var slotIds = (preLockCandidates.Count > 0
                        ? preLockCandidates.Select(s => s.Id)
                        : [selection.Slot.Id])
                    .Distinct()
                    .OrderBy(id => id)
                    .ToArray();

                // ADR-0059 single batch lock: patients (02) -> services (04) -> doctors (05, ASC) -> hospital_rooms (06, ASC) -> examination_slots (07, ASC)
                await _bookingLock.AcquireBatchAsync(
                    selection.PatientId,
                    selection.ServiceId,
                    doctorIds,
                    roomIds,
                    slotIds,
                    ct);

                var reloadedCandidates = await IncludeResourceGraph(_db.ExaminationSlots.AsNoTracking())
                    .Where(s => slotIds.Contains(s.Id))
                    .ToListAsync(ct);

                var minStartTime = selection.NowLocal.AddHours(selection.Policy.BookingLeadHours);
                var maxDate = selection.Today.AddDays(selection.Policy.BookingWindowDays);

                var openBookableCandidates = reloadedCandidates
                    .Where(s =>
                        s.ServiceId == selection.ServiceId &&
                        string.Equals(s.Status, "OPEN", StringComparison.OrdinalIgnoreCase) &&
                        s.StartsAt.HasValue &&
                        s.EndsAt.HasValue &&
                        s.StartsAt.Value > selection.NowLocal &&
                        s.StartsAt.Value >= minStartTime &&
                        DateOnly.FromDateTime(s.StartsAt.Value) <= maxDate &&
                        (selection.HasExplicitSlotId
                            ? s.StartsAt == selection.WindowStartsAt && s.EndsAt == selection.WindowEndsAt
                            : DateOnly.FromDateTime(s.StartsAt.Value) == selection.DateIso &&
                              (selection.IsMorningShift
                                  ? s.StartsAt.Value.TimeOfDay < AppointmentBookingRules.AfternoonShiftStart
                                  : s.StartsAt.Value.TimeOfDay >= AppointmentBookingRules.AfternoonShiftStart)))
                    .ToList();

                if (openBookableCandidates.Count == 0)
                {
                    throw new ConflictException(
                        selection.HasExplicitSlotId
                            ? "Khung giờ khám không còn mở để đặt lịch."
                            : "Ca khám đã hết chỗ. Vui lòng chọn ca hoặc ngày khác.");
                }

                var activeCandidates = await FilterSlotsByActiveResourcesAsync(
                    _db,
                    openBookableCandidates,
                    requireDoctorAuthorization: false,
                    ct);

                if (activeCandidates.Count == 0)
                {
                    throw new ConflictException(InactiveResourceConflictMessage);
                }

                var activeCandidateIds = activeCandidates.Select(s => s.Id).ToList();
                var bookedCounts = await _db.Appointments
                    .AsNoTracking()
                    .Where(a =>
                        a.SlotId != null &&
                        activeCandidateIds.Contains((long)a.SlotId) &&
                        (a.Status == null || a.Status.ToUpper() != AppointmentBookingRules.CancelledStatus))
                    .GroupBy(a => a.SlotId!.Value)
                    .Select(g => new { SlotId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.SlotId, x => x.Count, ct);

                var orderedCandidates = selection.HasExplicitSlotId
                    ? activeCandidates
                        .OrderBy(s => s.Id == selection.RequestedSlotId ? 0 : 1)
                        .ThenBy(s => s.Id)
                    : activeCandidates
                        .OrderBy(s => s.StartsAt)
                        .ThenBy(s => s.Id);

                slot = orderedCandidates.FirstOrDefault(s =>
                        bookedCounts.GetValueOrDefault(s.Id, 0) < (s.Capacity ?? 0))
                    ?? throw new ConflictException(
                        selection.HasExplicitSlotId
                            ? "Khung giờ khám vừa hết chỗ. Vui lòng chọn khung giờ khác."
                            : "Ca khám đã hết chỗ. Vui lòng chọn ca hoặc ngày khác.");

                await AppointmentBookingRules.EnsurePatientLimitsAsync(
                    _db,
                    selection.PatientId,
                    slot,
                    selection.NowLocal,
                    selection.Policy,
                    null,
                    ct);
            }

            appointment = new Appointment
            {
                PatientId = selection.PatientId,
                SlotId = slot.Id,
                BookingChannel = AppointmentBookingRules.OnlineChannel,
                Status = AppointmentBookingRules.BookedStatus,
                BookedByUserId = userId,
                BookedAt = selection.NowLocal,
                ReasonForVisit = selection.SymptomNote,
                IdempotencyKey = null,
                ReceptionNote = null
            };

            _db.Appointments.Add(appointment);
            await _db.SaveChangesAsync(ct);

            appointment.AppointmentCode = $"APT-{selection.NowLocal:yyyyMM}-{appointment.Id:D4}";
            appointmentIdStr = appointment.Id.ToString(CultureInfo.InvariantCulture);

            _audit.Add(
                userId,
                "APPOINTMENT_BOOK",
                "APPOINTMENT",
                appointmentIdStr,
                new { appointment.AppointmentCode, SlotId = slot.Id, PatientId = selection.PatientId },
                ipAddress);

            var authorizedEmail = await AppointmentNotificationComposer.ResolveAuthorizedRecipientEmailAsync(
                _db,
                selection.PatientId,
                userId,
                selection.NowLocal,
                ct);
            var bookedRoomLabel = slot.RoomId is long bookedRoomId
                ? await _db.HospitalRooms
                    .AsNoTracking()
                    .Where(r => r.Id == bookedRoomId)
                    .Select(r => !string.IsNullOrWhiteSpace(r.Name) ? r.Name : r.RoomCode)
                    .FirstOrDefaultAsync(ct)
                : null;
            _db.Notifications.Add(AppointmentNotificationComposer.CreateBookedNotification(
                patientId: selection.PatientId,
                appointmentId: appointment.Id,
                appointmentCode: appointment.AppointmentCode,
                serviceName: selection.Service.Name,
                startsAt: slot.StartsAt!.Value,
                endsAt: slot.EndsAt!.Value,
                roomLabel: bookedRoomLabel,
                authorizedEmail: authorizedEmail,
                nowLocal: selection.NowLocal));

            await _db.SaveChangesAsync(ct);

            queueNumber = await AppointmentBookingRules.ComputeQueueNumberAsync(_db, appointment.Id, slot, ct);

            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }
        }
        catch (Exception ex) when (IsConcurrencyOrLockConflict(ex))
        {
            await RollbackAndClearTrackerAsync(_db, tx, ct);
            throw new ConflictException("Khung giờ khám đang được thao tác đồng thời hoặc vừa hết chỗ. Vui lòng chọn lại và thử lại.");
        }

        if (DateOnly.FromDateTime(slot.StartsAt!.Value) == selection.Today && selection.HospitalId > 0)
        {
            await MedFlow.Application.Services.Realtime.RealtimePublishSafeExtensions.TryPublishQueueChangedSafeAsync(
                _realtimePublisher,
                new MedFlow.Application.DTOs.Realtime.QueueChangedNotification(
                    HospitalId: selection.HospitalId,
                    ServicePointId: null,
                    RoomId: slot.RoomId,
                    AppointmentId: appointment.Id),
                ct);
        }

        return new CreateAppointmentResponse(
            AppointmentId: appointmentIdStr,
            AppointmentCode: appointment.AppointmentCode,
            QueueNumber: queueNumber,
            Status: AppointmentBookingRules.BookedStatus);
    }

    public async Task<AppointmentReviewPreviewResponse> PreviewAsync(
        long userId,
        GetAppointmentReviewPreviewRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var isServicePreview = string.Equals(request.Mode?.Trim(), "service", StringComparison.OrdinalIgnoreCase);
        string? slotOrShiftId;
        if (isServicePreview)
        {
            var hasNumericSlotId = !string.IsNullOrWhiteSpace(request.SlotId) &&
                                   long.TryParse(request.SlotId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSlotId) &&
                                   parsedSlotId > 0;
            var hasLegacyShiftId = !string.IsNullOrWhiteSpace(request.ShiftId) &&
                                   (string.Equals(request.ShiftId.Trim(), "shift-morning", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(request.ShiftId.Trim(), "shift-afternoon", StringComparison.OrdinalIgnoreCase));
            slotOrShiftId = hasNumericSlotId && !hasLegacyShiftId
                ? request.SlotId
                : (!string.IsNullOrWhiteSpace(request.ShiftId) ? request.ShiftId : request.SlotId);
        }
        else
        {
            slotOrShiftId = request.SlotId;
        }

        var selection = await ResolveSelectionAsync(
            userId,
            new CreateAppointmentRequest
            {
                Mode = request.Mode,
                PatientId = request.PatientId,
                DoctorId = request.DoctorId,
                ServiceId = request.ServiceId,
                ServiceCode = request.ServiceCode,
                DateIso = request.DateIso,
                SlotOrShiftId = slotOrShiftId,
                SymptomNote = request.SymptomNote
            },
            ct);

        await AppointmentBookingRules.EnsureSlotHasCapacityAsync(_db, selection.Slot, ct);
        await AppointmentBookingRules.EnsurePatientLimitsAsync(_db, selection.PatientId, selection.Slot, selection.NowLocal, selection.Policy, null, ct);

        var doctor = selection.Doctor ?? (selection.Slot.DoctorId is long id
            ? await _doctorCatalog.GetByIdOrCodeAsync(id.ToString(CultureInfo.InvariantCulture), ct)
            : null);

        var room = selection.Slot.RoomId is long roomId
            ? await _db.HospitalRooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct)
            : null;

        var isSelf = selection.Owned.Self.Id == selection.PatientId;
        Patient patient;
        string? relationship = null;

        if (isSelf)
        {
            patient = selection.Owned.Self;
        }
        else
        {
            var link = selection.Owned.FamilyLinks.First(l => l.MemberPatientId == selection.PatientId);
            patient = link.MemberPatient!;
            relationship = link.Relationship;
        }

        return AppointmentReviewPreviewMapper.Map(
            patient,
            relationship,
            isSelf,
            selection.Owned.Self,
            selection.SymptomNote,
            selection.IsDoctorMode,
            selection.IsMorningShift,
            selection.Service,
            doctor,
            selection.Slot,
            room,
            selection.Policy,
            selection.Today,
            selection.HasExplicitSlotId);
    }

    internal static IQueryable<ExaminationSlot> IncludeResourceGraph(IQueryable<ExaminationSlot> query)
        => query
            .Include(s => s.Service)
                .ThenInclude(svc => svc!.Department)
            .Include(s => s.Room)
                .ThenInclude(r => r!.Department)
            .Include(s => s.Doctor)
                .ThenInclude(d => d!.Staff)
                    .ThenInclude(st => st!.Department);

    internal static async Task<List<ExaminationSlot>> FilterSlotsByActiveResourcesAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<ExaminationSlot> slots,
        bool requireDoctorAuthorization,
        CancellationToken ct)
    {
        if (slots.Count == 0)
        {
            return [];
        }

        var structurallyActive = slots
            .Where(s =>
            {
                if (s.ServiceId.HasValue)
                {
                    if (s.Service is null ||
                        s.Service.IsActive == false ||
                        s.Service.AllowsOnlineBooking != true)
                    {
                        return false;
                    }

                    if (s.Service.DepartmentId.HasValue &&
                        (s.Service.Department is null || s.Service.Department.IsActive == false))
                    {
                        return false;
                    }
                }

                if (s.RoomId.HasValue)
                {
                    if (s.Room is null ||
                        !string.Equals(s.Room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    if (s.Room.DepartmentId.HasValue &&
                        (s.Room.Department is null || s.Room.Department.IsActive == false))
                    {
                        return false;
                    }
                }

                if (s.DoctorId.HasValue)
                {
                    if (s.Doctor is null || !s.Doctor.IsActive)
                    {
                        return false;
                    }

                    if (s.Doctor.StaffId.HasValue)
                    {
                        if (s.Doctor.Staff is null ||
                            !string.Equals(s.Doctor.Staff.Status ?? "ACTIVE", "ACTIVE", StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }

                        if (s.Doctor.Staff.DepartmentId.HasValue &&
                            (s.Doctor.Staff.Department is null || s.Doctor.Staff.Department.IsActive == false))
                        {
                            return false;
                        }
                    }
                }

                return true;
            })
            .ToList();

        if (structurallyActive.Count == 0)
        {
            return [];
        }

        var staffUserIds = structurallyActive
            .Where(s => s.Doctor?.Staff?.UserId is not null)
            .Select(s => s.Doctor!.Staff!.UserId!.Value)
            .Distinct()
            .ToList();

        HashSet<long>? inactiveUserIds = null;
        if (staffUserIds.Count > 0)
        {
            try
            {
                inactiveUserIds = (await db.Users
                        .AsNoTracking()
                        .Where(u => staffUserIds.Contains(u.Id) && u.AccountStatus != AccountStatus.ACTIVE)
                        .Select(u => u.Id)
                        .ToListAsync(ct))
                    .ToHashSet();
            }
            catch (NotSupportedException)
            {
                inactiveUserIds = null;
            }
        }

        if (inactiveUserIds is { Count: > 0 })
        {
            structurallyActive = structurallyActive
                .Where(s => s.Doctor?.Staff?.UserId is not long uid || !inactiveUserIds.Contains(uid))
                .ToList();
        }

        if (!requireDoctorAuthorization || structurallyActive.Count == 0)
        {
            return structurallyActive;
        }

        var doctorIds = structurallyActive
            .Where(s => s.DoctorId.HasValue && s.ServiceId.HasValue)
            .Select(s => s.DoctorId!.Value)
            .Distinct()
            .ToList();
        var serviceIds = structurallyActive
            .Where(s => s.DoctorId.HasValue && s.ServiceId.HasValue)
            .Select(s => s.ServiceId!.Value)
            .Distinct()
            .ToList();

        if (doctorIds.Count == 0 || serviceIds.Count == 0)
        {
            return structurallyActive;
        }

        try
        {
            var authorizedPairs = (await db.DoctorServiceAuthorizations
                    .AsNoTracking()
                    .Where(a =>
                        doctorIds.Contains(a.DoctorId) &&
                        serviceIds.Contains(a.ServiceId) &&
                        a.IsActive == true)
                    .Select(a => new { a.DoctorId, a.ServiceId })
                    .ToListAsync(ct))
                .Select(x => (x.DoctorId, x.ServiceId))
                .ToHashSet();

            return structurallyActive
                .Where(s =>
                    !s.DoctorId.HasValue ||
                    !s.ServiceId.HasValue ||
                    authorizedPairs.Contains((s.DoctorId.Value, s.ServiceId.Value)))
                .ToList();
        }
        catch (NotSupportedException)
        {
            return structurallyActive;
        }
    }

    internal static bool IsRelationalDatabase(IApplicationDbContext db)
        => db is DbContext efDb &&
           !string.IsNullOrWhiteSpace(efDb.Database.ProviderName) &&
           !efDb.Database.ProviderName.Contains("InMemory", StringComparison.OrdinalIgnoreCase);

    internal static async Task<IDbContextTransaction?> BeginTransactionSafeAsync(
        IApplicationDbContext db,
        CancellationToken ct)
    {
        if (db is DbContext efDb)
        {
            if (!IsRelationalDatabase(db))
            {
                try
                {
                    return await db.BeginTransactionAsync(ct);
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }

            if (efDb.Database.CurrentTransaction is not null)
            {
                return null;
            }
        }

        return await db.BeginTransactionAsync(ct);
    }

    internal static bool IsConcurrencyOrLockConflict(Exception ex)
        => ex is DbUpdateConcurrencyException ||
           PostgresConcurrencyException.TryGetSqlState(ex) is
               PostgresConcurrencyException.UniqueViolationSqlState or
               PostgresConcurrencyException.DeadlockDetectedSqlState or
               PostgresConcurrencyException.SerializationFailureSqlState or
               PostgresConcurrencyException.LockNotAvailableSqlState;

    internal static async Task RollbackAndClearTrackerAsync(
        IApplicationDbContext db,
        IDbContextTransaction? tx,
        CancellationToken ct)
    {
        if (tx is not null)
        {
            try
            {
                await tx.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Transaction may already be aborted by PostgreSQL on 40P01/40001/23505.
            }
        }

        if (db is DbContext efDb)
        {
            efDb.ChangeTracker.Clear();
        }
    }

    private async Task<BookingSelection> ResolveSelectionAsync(
        long userId,
        CreateAppointmentRequest request,
        CancellationToken ct)
    {
        if (userId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        ArgumentNullException.ThrowIfNull(request);

        var mode = request.Mode?.Trim();
        var isDoctorMode = string.Equals(mode, "doctor", StringComparison.OrdinalIgnoreCase);
        var isServiceMode = string.Equals(mode, "service", StringComparison.OrdinalIgnoreCase);
        if (!isDoctorMode && !isServiceMode)
            throw new ValidationException("Hình thức đặt lịch không hợp lệ.");

        if (string.IsNullOrWhiteSpace(request.PatientId) ||
            !long.TryParse(request.PatientId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var patientId) ||
            patientId <= 0)
        {
            throw new ValidationException("Vui lòng chọn hồ sơ người bệnh thăm khám.");
        }

        if (string.IsNullOrWhiteSpace(request.ServiceId))
            throw new ValidationException("Vui lòng chọn dịch vụ khám.");

        if (string.IsNullOrWhiteSpace(request.DateIso) ||
            !DateOnly.TryParseExact(request.DateIso.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateIso))
        {
            throw new ValidationException("Ngày khám không hợp lệ (định dạng yyyy-MM-dd).");
        }

        var symptomNote = string.IsNullOrWhiteSpace(request.SymptomNote)
            ? null
            : request.SymptomNote.Trim();

        if (request.SymptomNote is not null && (symptomNote is null || symptomNote.Length < 3))
            throw new ValidationException("Vui lòng nhập lý do khám hoặc triệu chứng ban đầu (tối thiểu 3 ký tự).");

        if (symptomNote is not null && symptomNote.Length > MaxSymptomNoteLength)
            throw new ValidationException("Lý do khám tối đa 500 ký tự.");

        long requestedSlotId = 0;
        var isMorningShift = false;

        if (isDoctorMode)
        {
            if (string.IsNullOrWhiteSpace(request.DoctorId))
                throw new ValidationException("Vui lòng chọn bác sĩ.");

            if (string.IsNullOrWhiteSpace(request.SlotOrShiftId) ||
                !long.TryParse(request.SlotOrShiftId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out requestedSlotId) ||
                requestedSlotId <= 0)
            {
                throw new ValidationException("Khung giờ khám không hợp lệ.");
            }
        }
        else
        {
            var shiftOrSlot = request.SlotOrShiftId?.Trim();
            isMorningShift = string.Equals(shiftOrSlot, "shift-morning", StringComparison.OrdinalIgnoreCase);
            var isAfternoonShift = string.Equals(shiftOrSlot, "shift-afternoon", StringComparison.OrdinalIgnoreCase);

            if (!isMorningShift && !isAfternoonShift)
            {
                if (string.IsNullOrWhiteSpace(shiftOrSlot) ||
                    !long.TryParse(shiftOrSlot, NumberStyles.Integer, CultureInfo.InvariantCulture, out requestedSlotId) ||
                    requestedSlotId <= 0)
                {
                    throw new ValidationException("Khung giờ khám không hợp lệ.");
                }
            }
        }

        var serviceDto = await _serviceCatalog.GetByIdOrCodeAsync(request.ServiceId, ct);
        if (!string.IsNullOrWhiteSpace(request.ServiceCode) &&
            !string.Equals(request.ServiceCode.Trim(), serviceDto.ServiceCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Mã dịch vụ khám không khớp với dịch vụ đã chọn.");
        }

        if (!serviceDto.IsOnlineBookingActive)
            throw new ValidationException("Dịch vụ khám này không hỗ trợ đặt lịch trực tuyến.");

        var serviceId = long.Parse(serviceDto.Id, CultureInfo.InvariantCulture);
        long? doctorId = null;
        DoctorDetailDto? doctorDto = null;

        if (isDoctorMode)
        {
            doctorDto = await _doctorCatalog.GetByIdOrCodeAsync(request.DoctorId!, ct);
            var resolvedDoctorId = long.Parse(doctorDto.Id, CultureInfo.InvariantCulture);
            doctorId = resolvedDoctorId;

            var isAuthorized = await _db.DoctorServiceAuthorizations
                .AsNoTracking()
                .AnyAsync(a => a.DoctorId == resolvedDoctorId && a.ServiceId == serviceId && a.IsActive == true, ct);

            if (!isAuthorized)
                throw new ValidationException("Bác sĩ không thực hiện dịch vụ khám này.");
        }

        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var owned = await AppointmentBookingRules.LoadOwnedPatientsAsync(_db, userId, nowLocal, ct);
        var isOwned = owned.Self.Id == patientId ||
                      owned.FamilyLinks.Any(l => l.MemberPatientId == patientId);

        if (!isOwned)
            throw new ForbiddenException("Bạn không có quyền đặt lịch khám cho hồ sơ không thuộc quyền quản lý.");

        var hospitalId = await AppointmentBookingRules.ResolveHospitalIdAsync(_db, serviceId, doctorId, ct);
        var policy = await _policyService.GetEffectiveAsync(hospitalId, ct);

        ExaminationSlot preselectedSlot;
        DateTime windowStartsAt;
        DateTime windowEndsAt;

        if (isDoctorMode)
        {
            var candidate = await IncludeResourceGraph(_db.ExaminationSlots.AsNoTracking())
                .FirstOrDefaultAsync(s => s.Id == requestedSlotId, ct)
                ?? throw new NotFoundException("ExaminationSlot", requestedSlotId);

            if (candidate.DoctorId != doctorId!.Value ||
                candidate.ServiceId != serviceId ||
                candidate.StartsAt is null ||
                DateOnly.FromDateTime(candidate.StartsAt.Value) != dateIso)
            {
                throw new ValidationException("Khung giờ khám không khớp với bác sĩ, dịch vụ hoặc ngày đã chọn.");
            }

            AppointmentBookingRules.EnsureSlotBookable(candidate, nowLocal, today, policy);

            var activeCheck = await FilterSlotsByActiveResourcesAsync(_db, [candidate], requireDoctorAuthorization: true, ct);
            if (activeCheck.Count == 0)
            {
                throw new ConflictException(InactiveResourceConflictMessage);
            }

            isMorningShift = candidate.StartsAt.Value.TimeOfDay < AppointmentBookingRules.AfternoonShiftStart;
            preselectedSlot = candidate;
            windowStartsAt = candidate.StartsAt.Value;
            windowEndsAt = candidate.EndsAt!.Value;
        }
        else if (requestedSlotId > 0)
        {
            var anchor = await IncludeResourceGraph(_db.ExaminationSlots.AsNoTracking())
                .FirstOrDefaultAsync(s => s.Id == requestedSlotId, ct)
                ?? throw new NotFoundException("ExaminationSlot", requestedSlotId);

            if (anchor.ServiceId != serviceId ||
                anchor.StartsAt is null ||
                anchor.EndsAt is null ||
                DateOnly.FromDateTime(anchor.StartsAt.Value) != dateIso)
            {
                throw new ValidationException("Khung giờ khám không khớp với bác sĩ, dịch vụ hoặc ngày đã chọn.");
            }

            if (anchor.StartsAt.Value <= nowLocal)
                throw new ValidationException(AppointmentBookingRules.BookingSlotStartedMessage);
            if (anchor.StartsAt.Value < nowLocal.AddHours(policy.BookingLeadHours))
                throw new ValidationException($"Chỉ được đặt lịch trước giờ khám tối thiểu {policy.BookingLeadHours} giờ.");
            if (DateOnly.FromDateTime(anchor.StartsAt.Value) > today.AddDays(policy.BookingWindowDays))
                throw new ValidationException($"Chỉ được đặt lịch trong vòng {policy.BookingWindowDays} ngày tới.");

            windowStartsAt = anchor.StartsAt.Value;
            windowEndsAt = anchor.EndsAt.Value;
            isMorningShift = windowStartsAt.TimeOfDay < AppointmentBookingRules.AfternoonShiftStart;

            var windowOpenSlots = await IncludeResourceGraph(_db.ExaminationSlots.AsNoTracking())
                .Where(s =>
                    s.ServiceId == serviceId &&
                    s.Status.ToUpper() == "OPEN" &&
                    s.StartsAt == windowStartsAt &&
                    s.EndsAt == windowEndsAt)
                .OrderBy(s => s.Id == requestedSlotId ? 0 : 1)
                .ThenBy(s => s.Id)
                .ToListAsync(ct);

            if (windowOpenSlots.Count == 0)
                throw new ConflictException("Khung giờ khám không còn mở để đặt lịch.");

            var activeWindowSlots = await FilterSlotsByActiveResourcesAsync(
                _db,
                windowOpenSlots,
                requireDoctorAuthorization: false,
                ct);

            if (activeWindowSlots.Count == 0)
                throw new ConflictException(InactiveResourceConflictMessage);

            var windowSlotIds = activeWindowSlots.Select(s => s.Id).ToList();
            var bookedCounts = await _db.Appointments
                .AsNoTracking()
                .Where(a =>
                    a.SlotId != null &&
                    windowSlotIds.Contains((long)a.SlotId) &&
                    (a.Status == null || a.Status.ToUpper() != AppointmentBookingRules.CancelledStatus))
                .GroupBy(a => a.SlotId!.Value)
                .Select(g => new { SlotId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SlotId, x => x.Count, ct);

            preselectedSlot = activeWindowSlots.FirstOrDefault(s =>
                bookedCounts.GetValueOrDefault(s.Id, 0) < (s.Capacity ?? 0))
                ?? activeWindowSlots[0];
        }
        else
        {
            preselectedSlot = await SelectServiceShiftSlotAsync(
                serviceId,
                dateIso,
                isMorningShift,
                nowLocal,
                today,
                policy,
                ct);
            windowStartsAt = preselectedSlot.StartsAt!.Value;
            windowEndsAt = preselectedSlot.EndsAt!.Value;
        }

        return new BookingSelection(
            IsDoctorMode: isDoctorMode,
            IsMorningShift: isMorningShift,
            HasExplicitSlotId: isDoctorMode || requestedSlotId > 0,
            RequestedSlotId: requestedSlotId,
            ServiceId: serviceId,
            DateIso: dateIso,
            WindowStartsAt: windowStartsAt,
            WindowEndsAt: windowEndsAt,
            HospitalId: hospitalId,
            PatientId: patientId,
            SymptomNote: symptomNote,
            Service: serviceDto,
            Doctor: doctorDto,
            Owned: owned,
            Policy: policy,
            Slot: preselectedSlot,
            NowLocal: nowLocal,
            Today: today);
    }

    private async Task<List<ExaminationSlot>> LoadRawServiceShiftCandidatesAsync(
        long serviceId,
        DateOnly dateIso,
        bool isMorningShift,
        DateTime nowLocal,
        DateOnly today,
        AppointmentPolicyDto policy,
        CancellationToken ct)
    {
        var dayStart = dateIso.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var dayEnd = dayStart.AddDays(1);

        var rawSlots = await IncludeResourceGraph(_db.ExaminationSlots.AsNoTracking())
            .Where(s =>
                s.ServiceId == serviceId &&
                s.Status.ToUpper() == "OPEN" &&
                s.StartsAt != null &&
                s.EndsAt != null &&
                s.StartsAt >= dayStart &&
                s.StartsAt < dayEnd)
            .OrderBy(s => s.StartsAt)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        var minStartTime = nowLocal.AddHours(policy.BookingLeadHours);
        var maxDate = today.AddDays(policy.BookingWindowDays);

        return rawSlots
            .Where(s =>
            {
                var startsAt = s.StartsAt!.Value;
                var inShift = isMorningShift
                    ? startsAt.TimeOfDay < AppointmentBookingRules.AfternoonShiftStart
                    : startsAt.TimeOfDay >= AppointmentBookingRules.AfternoonShiftStart;

                return inShift &&
                       startsAt > nowLocal &&
                       startsAt >= minStartTime &&
                       DateOnly.FromDateTime(startsAt) <= maxDate;
            })
            .ToList();
    }

    private async Task<ExaminationSlot> SelectServiceShiftSlotAsync(
        long serviceId,
        DateOnly dateIso,
        bool isMorningShift,
        DateTime nowLocal,
        DateOnly today,
        AppointmentPolicyDto policy,
        CancellationToken ct)
    {
        var candidates = await LoadRawServiceShiftCandidatesAsync(
            serviceId,
            dateIso,
            isMorningShift,
            nowLocal,
            today,
            policy,
            ct);

        if (candidates.Count == 0)
            throw new ConflictException("Ca khám đã hết chỗ. Vui lòng chọn ca hoặc ngày khác.");

        var activeCandidates = await FilterSlotsByActiveResourcesAsync(
            _db,
            candidates,
            requireDoctorAuthorization: false,
            ct);

        if (activeCandidates.Count == 0)
            throw new ConflictException(InactiveResourceConflictMessage);

        var candidateIds = activeCandidates.Select(s => s.Id).ToList();
        var bookedCounts = await _db.Appointments
            .AsNoTracking()
            .Where(a =>
                a.SlotId != null &&
                candidateIds.Contains((long)a.SlotId) &&
                (a.Status == null || a.Status.ToUpper() != AppointmentBookingRules.CancelledStatus))
            .GroupBy(a => a.SlotId!.Value)
            .Select(g => new { SlotId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SlotId, x => x.Count, ct);

        var selected = activeCandidates.FirstOrDefault(s =>
            (s.Capacity ?? 0) - bookedCounts.GetValueOrDefault(s.Id, 0) > 0);

        if (selected is null)
            throw new ConflictException("Ca khám đã hết chỗ. Vui lòng chọn ca hoặc ngày khác.");

        return selected;
    }

    private sealed record BookingSelection(
        bool IsDoctorMode,
        bool IsMorningShift,
        bool HasExplicitSlotId,
        long RequestedSlotId,
        long ServiceId,
        DateOnly DateIso,
        DateTime WindowStartsAt,
        DateTime WindowEndsAt,
        long HospitalId,
        long PatientId,
        string? SymptomNote,
        ExaminationServiceDto Service,
        DoctorDetailDto? Doctor,
        AppointmentBookingRules.OwnedPatients Owned,
        AppointmentPolicyDto Policy,
        ExaminationSlot Slot,
        DateTime NowLocal,
        DateOnly Today);
}
