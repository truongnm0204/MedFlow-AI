using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Reception;

public sealed class ReceptionPatientSearchService : IReceptionPatientSearchService
{
    private const string TabAll = "ALL";
    private const string TabHasAppointment = "HAS_APPOINTMENT";
    private const string TabCheckedIn = "CHECKED_IN";
    private const string TabNoAppointment = "NO_APPOINTMENT";
    private const int MaxQueryLength = 100;
    private const int MaxPageSize = 100;
    private const string CompletedStatus = "COMPLETED";
    private const string NotUpdated = "Chưa cập nhật";

    private static readonly HashSet<string> AllowedRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        RoleNames.Reception,
        RoleNames.HospitalAdmin,
        RoleNames.SystemAdmin,
    };

    private static readonly HashSet<string> AllowedPermissionCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        PermissionCodes.PatientCheckin,
        PermissionCodes.PatientProfileRead,
        PermissionCodes.VisitAssign,
        PermissionCodes.QueueManage,
        PermissionCodes.HospitalConfig,
        PermissionCodes.UserManage,
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public ReceptionPatientSearchService(IApplicationDbContext db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ReceptionDashboardSummaryDto> GetDashboardSummaryAsync(
        long actorUserId,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var scopedHospitalId = await EnsureReceptionAccessAndResolveHospitalIdAsync(actorUserId, nowUtc, ct);
        if (scopedHospitalId is <= 0)
        {
            return new ReceptionDashboardSummaryDto(0, 0, 0, 0);
        }

        var today = HospitalClock.GetToday(_timeProvider);
        var todayStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var todayEnd = todayStart.AddDays(1);

        return await ComputeDashboardSummaryCoreAsync(scopedHospitalId, todayStart, todayEnd, ct);
    }

    public async Task<SearchPatientsResponseDto> SearchPatientsAsync(
        SearchPatientsRequest request,
        CancellationToken ct = default,
        long? actorUserId = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tab = NormalizeTab(request.Tab);
        if (request.Page < 1)
            throw new ValidationException("Page phải lớn hơn hoặc bằng 1.");
        if (request.PageSize < 1 || request.PageSize > MaxPageSize)
            throw new ValidationException($"PageSize phải nằm trong khoảng 1 đến {MaxPageSize}.");

        var term = request.Query?.Trim();
        if (term is { Length: > MaxQueryLength })
            throw new ValidationException($"Từ khóa tìm kiếm tối đa {MaxQueryLength} ký tự.");

        long? scopedHospitalId = null;
        if (actorUserId.HasValue)
        {
            var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
            scopedHospitalId = await EnsureReceptionAccessAndResolveHospitalIdAsync(actorUserId.Value, nowUtc, ct);
        }

        var today = HospitalClock.GetToday(_timeProvider);
        var todayStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var todayEnd = todayStart.AddDays(1);

        var todayAppointments = _db.Appointments.AsNoTracking()
            .Where(a => a.PatientId != null
                && a.Slot != null
                && a.Slot.StartsAt >= todayStart
                && a.Slot.StartsAt < todayEnd
                && a.Status != AppointmentBookingRules.CancelledStatus
                && a.Status != AppointmentBookingRules.NoShowStatus);

        if (scopedHospitalId is > 0)
        {
            var hId = scopedHospitalId.Value;
            todayAppointments = todayAppointments.Where(a =>
                (a.Slot!.Service != null && a.Slot.Service.HospitalId == hId) ||
                (a.Slot.Service == null && a.Slot.Room != null && a.Slot.Room.HospitalId == hId) ||
                (a.Slot.Service == null && a.Slot.Room == null && a.Slot.Doctor != null && a.Slot.Doctor.HospitalId == hId) ||
                (a.Slot.Room != null && a.Slot.Room.HospitalId == hId) ||
                (a.Slot.Doctor != null && a.Slot.Doctor.HospitalId == hId));
        }

        var patients = _db.Patients.AsNoTracking().Where(p => p.Status == "ACTIVE");

        if (!string.IsNullOrEmpty(term))
        {
            var lowered = term.ToLowerInvariant();
            if (IsIdentityNumber(term))
            {
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(term))).ToLowerInvariant();
                patients = patients.Where(p =>
                    p.PatientCode.ToLower().Contains(lowered)
                    || p.FullName.ToLower().Contains(lowered)
                    || (p.Phone != null && p.Phone.ToLower().Contains(lowered))
                    || p.IdentificationHash == hash);
            }
            else
            {
                patients = patients.Where(p =>
                    p.PatientCode.ToLower().Contains(lowered)
                    || p.FullName.ToLower().Contains(lowered)
                    || (p.Phone != null && p.Phone.ToLower().Contains(lowered)));
            }
        }

        var hasAppointment = patients.Where(p => todayAppointments.Any(a => a.PatientId == p.Id));
        var checkedIn = patients.Where(p => todayAppointments.Any(a =>
            a.PatientId == p.Id && a.Status == AppointmentBookingRules.CheckedInStatus));
        var noAppointment = patients.Where(p => !todayAppointments.Any(a => a.PatientId == p.Id));

        var counts = new SearchTabCountsDto(
            await patients.CountAsync(ct),
            await hasAppointment.CountAsync(ct),
            await checkedIn.CountAsync(ct),
            await noAppointment.CountAsync(ct));

        var filtered = tab switch
        {
            TabHasAppointment => hasAppointment,
            TabCheckedIn => checkedIn,
            TabNoAppointment => noAppointment,
            _ => patients,
        };

        var totalCount = tab switch
        {
            TabHasAppointment => counts.HasAppointment,
            TabCheckedIn => counts.CheckedIn,
            TabNoAppointment => counts.NoAppointment,
            _ => counts.All,
        };

        var pagePatients = await filtered
            .OrderBy(p => p.FullName)
            .ThenBy(p => p.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var pageIds = pagePatients.Select(p => p.Id).ToList();

        var appointmentRows = await todayAppointments
            .Where(a => pageIds.Contains(a.PatientId!.Value))
            .Select(a => new
            {
                a.Id,
                PatientId = a.PatientId!.Value,
                a.Status,
                StartsAt = a.Slot!.StartsAt,
                DoctorName = a.Slot.Doctor != null && a.Slot.Doctor.Staff != null
                    ? a.Slot.Doctor.Staff.FullName
                    : null,
                ServiceDepartment = a.Slot.Service != null && a.Slot.Service.Department != null
                    ? a.Slot.Service.Department.Name
                    : null,
                RoomName = a.Slot.Room != null ? a.Slot.Room.Name : null,
                RoomDepartment = a.Slot.Room != null && a.Slot.Room.Department != null
                    ? a.Slot.Room.Department.Name
                    : null,
            })
            .ToListAsync(ct);

        // One summary per patient: prefer the checked-in appointment, otherwise the earliest one.
        var todayByPatient = appointmentRows
            .GroupBy(r => r.PatientId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(r => r.Status == AppointmentBookingRules.CheckedInStatus)
                    .ThenBy(r => r.StartsAt)
                    .ThenBy(r => r.Id)
                    .First());

        var visitCounts = await _db.Visits.AsNoTracking()
            .Where(v => v.PatientId != null && pageIds.Contains(v.PatientId.Value) && v.Status == CompletedStatus)
            .GroupBy(v => v.PatientId!.Value)
            .Select(g => new { PatientId = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.PatientId, x => x.Total, ct);

        var items = pagePatients
            .Select(p =>
            {
                TodayAppointmentSummaryDto? todaySummary = null;
                if (todayByPatient.TryGetValue(p.Id, out var row))
                {
                    todaySummary = new TodayAppointmentSummaryDto(
                        row.Id,
                        row.StartsAt?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty,
                        row.DoctorName ?? NotUpdated,
                        row.ServiceDepartment ?? row.RoomDepartment ?? NotUpdated,
                        row.RoomName ?? NotUpdated,
                        row.Status ?? string.Empty);
                }

                return new PatientSearchResultItemDto(
                    p.Id,
                    p.PatientCode,
                    p.FullName,
                    p.Gender,
                    p.DateOfBirth,
                    string.IsNullOrWhiteSpace(p.IdentificationHash) ? null : PatientProfileService.MaskedVerifiedIdentityText,
                    p.Phone,
                    p.Address,
                    todaySummary,
                    visitCounts.GetValueOrDefault(p.Id));
            })
            .ToList();

        var dashboardSummary = scopedHospitalId is <= 0 && actorUserId.HasValue
            ? new ReceptionDashboardSummaryDto(0, 0, 0, 0)
            : await ComputeDashboardSummaryCoreAsync(scopedHospitalId, todayStart, todayEnd, ct);

        return new SearchPatientsResponseDto(
            items,
            totalCount,
            request.Page,
            request.PageSize,
            counts,
            dashboardSummary);
    }

    private async Task<ReceptionDashboardSummaryDto> ComputeDashboardSummaryCoreAsync(
        long? hospitalId,
        DateTime todayStart,
        DateTime todayEnd,
        CancellationToken ct)
    {
        Dictionary<long, long> roomHospitalMap = [];
        Dictionary<long, long> doctorHospitalMap = [];
        Dictionary<long, long> staffHospitalMap = [];
        Dictionary<long, long> serviceHospitalMap = [];

        if (hospitalId.HasValue)
        {
            roomHospitalMap = await _db.HospitalRooms
                .AsNoTracking()
                .Select(r => new
                {
                    r.Id,
                    HospitalId = r.HospitalId ?? (r.Department != null ? r.Department.HospitalId : null),
                })
                .Where(x => x.HospitalId.HasValue)
                .ToDictionaryAsync(x => x.Id, x => x.HospitalId!.Value, ct);

            doctorHospitalMap = await _db.Doctors
                .AsNoTracking()
                .Select(d => new
                {
                    d.Id,
                    HospitalId = d.HospitalId ?? (d.Staff != null ? d.Staff.HospitalId : null),
                })
                .Where(x => x.HospitalId.HasValue)
                .ToDictionaryAsync(x => x.Id, x => x.HospitalId!.Value, ct);

            staffHospitalMap = await _db.Staff
                .AsNoTracking()
                .Where(s => s.HospitalId.HasValue)
                .Select(s => new { s.Id, HospitalId = s.HospitalId!.Value })
                .ToDictionaryAsync(x => x.Id, x => x.HospitalId, ct);

            serviceHospitalMap = await _db.Services
                .AsNoTracking()
                .Select(s => new
                {
                    s.Id,
                    HospitalId = s.HospitalId ?? (s.Department != null ? s.Department.HospitalId : null),
                })
                .Where(x => x.HospitalId.HasValue)
                .ToDictionaryAsync(x => x.Id, x => x.HospitalId!.Value, ct);
        }

        var todayAppointments = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.PatientId != null
                && a.Slot != null
                && a.Slot.StartsAt >= todayStart
                && a.Slot.StartsAt < todayEnd)
            .Select(a => new
            {
                a.Id,
                a.Status,
                RoomId = a.Slot!.RoomId,
                DoctorId = a.Slot.DoctorId,
                ServiceId = a.Slot.ServiceId,
            })
            .ToListAsync(ct);

        var scopedTodayAppointments = hospitalId.HasValue
            ? todayAppointments
                .Where(a => ResolveSlotHospitalId(
                    a.RoomId,
                    a.DoctorId,
                    a.ServiceId,
                    roomHospitalMap,
                    doctorHospitalMap,
                    serviceHospitalMap) == hospitalId.Value)
                .ToList()
            : todayAppointments;

        var todayAppointmentIds = todayAppointments.Select(a => a.Id).ToHashSet();

        var candidateVisits = await _db.Visits
            .AsNoTracking()
            .Where(v =>
                (v.CheckedInAt != null && v.CheckedInAt >= todayStart && v.CheckedInAt < todayEnd) ||
                (v.CompletedAt != null && v.CompletedAt >= todayStart && v.CompletedAt < todayEnd) ||
                (v.AppointmentId != null && todayAppointmentIds.Contains(v.AppointmentId.Value)))
            .Select(v => new
            {
                v.Id,
                v.AppointmentId,
                v.ExaminationRoomId,
                v.AssignedDoctorId,
                v.CompletedByDoctorId,
                v.CheckedInByStaffId,
                v.Status,
                v.CheckedInAt,
                v.CompletedAt,
            })
            .ToListAsync(ct);

        Dictionary<long, long> appointmentHospitalMap = [];
        if (hospitalId.HasValue)
        {
            var referencedApptIds = candidateVisits
                .Where(v => v.AppointmentId.HasValue)
                .Select(v => v.AppointmentId!.Value)
                .Distinct()
                .ToList();

            if (referencedApptIds.Count > 0)
            {
                var apptSlotRows = await _db.Appointments
                    .AsNoTracking()
                    .Where(a => referencedApptIds.Contains(a.Id) && a.Slot != null)
                    .Select(a => new
                    {
                        a.Id,
                        RoomId = a.Slot!.RoomId,
                        DoctorId = a.Slot.DoctorId,
                        ServiceId = a.Slot.ServiceId,
                    })
                    .ToListAsync(ct);

                foreach (var row in apptSlotRows)
                {
                    var resolvedHosp = ResolveSlotHospitalId(
                        row.RoomId,
                        row.DoctorId,
                        row.ServiceId,
                        roomHospitalMap,
                        doctorHospitalMap,
                        serviceHospitalMap);
                    if (resolvedHosp.HasValue)
                    {
                        appointmentHospitalMap[row.Id] = resolvedHosp.Value;
                    }
                }
            }
        }

        var nonCancelledVisitApptIds = candidateVisits
            .Where(v => v.AppointmentId.HasValue &&
                        AppointmentBookingRules.NormalizeStatusKey(v.Status) is not ("CANCELLED" or "NO_SHOW"))
            .Select(v => v.AppointmentId!.Value)
            .ToHashSet();

        var waitingCheckIn = scopedTodayAppointments.Count(a =>
        {
            var statusKey = AppointmentBookingRules.NormalizeStatusKey(a.Status);
            if (string.IsNullOrEmpty(statusKey))
            {
                statusKey = AppointmentBookingRules.BookedStatus;
            }

            return statusKey is "BOOKED" or "CONFIRMED" &&
                   !nonCancelledVisitApptIds.Contains(a.Id);
        });

        var inQueue = 0;
        var inConsultation = 0;
        var completedToday = 0;

        foreach (var v in candidateVisits)
        {
            if (hospitalId.HasValue)
            {
                var visitHospitalId = ResolveVisitHospitalId(
                    v.ExaminationRoomId,
                    v.AssignedDoctorId,
                    v.CompletedByDoctorId,
                    v.CheckedInByStaffId,
                    v.AppointmentId,
                    roomHospitalMap,
                    doctorHospitalMap,
                    staffHospitalMap,
                    appointmentHospitalMap);

                if (visitHospitalId != hospitalId.Value)
                {
                    continue;
                }
            }

            var vStatus = AppointmentBookingRules.NormalizeStatusKey(v.Status);
            if (string.IsNullOrEmpty(vStatus) || vStatus is "CANCELLED" or "NO_SHOW")
            {
                continue;
            }

            if (vStatus == CompletedStatus)
            {
                // Strictly require CompletedAt within [todayStart, todayEnd); never infer from CheckedInAt.
                if (v.CompletedAt.HasValue &&
                    v.CompletedAt.Value >= todayStart &&
                    v.CompletedAt.Value < todayEnd)
                {
                    completedToday++;
                }

                continue;
            }

            var checkedInToday = v.CheckedInAt.HasValue &&
                                 v.CheckedInAt.Value >= todayStart &&
                                 v.CheckedInAt.Value < todayEnd;
            if (!checkedInToday)
            {
                continue;
            }

            if (vStatus == "IN_PROGRESS")
            {
                // Actively being examined by a doctor (excludes AWAITING_RESULTS diagnostic waits).
                inConsultation++;
            }
            else if (vStatus is "CHECKED_IN" or "WAITING" or "CALLED" or "RETURN_CONSULTATION")
            {
                // Checked-in waiting to be called/seen or waiting for return consultation after diagnostics.
                inQueue++;
            }
        }

        return new ReceptionDashboardSummaryDto(
            WaitingCheckIn: waitingCheckIn,
            InQueue: inQueue,
            InConsultation: inConsultation,
            CompletedToday: completedToday);
    }

    private static long? ResolveSlotHospitalId(
        long? roomId,
        long? doctorId,
        long? serviceId,
        IReadOnlyDictionary<long, long> roomHospitalMap,
        IReadOnlyDictionary<long, long> doctorHospitalMap,
        IReadOnlyDictionary<long, long> serviceHospitalMap)
    {
        if (serviceId.HasValue && serviceHospitalMap.TryGetValue(serviceId.Value, out var svcHosp))
            return svcHosp;
        if (roomId.HasValue && roomHospitalMap.TryGetValue(roomId.Value, out var roomHosp))
            return roomHosp;
        if (doctorId.HasValue && doctorHospitalMap.TryGetValue(doctorId.Value, out var docHosp))
            return docHosp;
        return null;
    }

    private static long? ResolveVisitHospitalId(
        long? roomId,
        long? assignedDoctorId,
        long? completedByDoctorId,
        long? checkedInByStaffId,
        long? appointmentId,
        IReadOnlyDictionary<long, long> roomHospitalMap,
        IReadOnlyDictionary<long, long> doctorHospitalMap,
        IReadOnlyDictionary<long, long> staffHospitalMap,
        IReadOnlyDictionary<long, long> appointmentHospitalMap)
    {
        if (roomId.HasValue && roomHospitalMap.TryGetValue(roomId.Value, out var roomHosp))
            return roomHosp;
        if (assignedDoctorId.HasValue && doctorHospitalMap.TryGetValue(assignedDoctorId.Value, out var docHosp))
            return docHosp;
        if (completedByDoctorId.HasValue && doctorHospitalMap.TryGetValue(completedByDoctorId.Value, out var completedDocHosp))
            return completedDocHosp;
        if (appointmentId.HasValue && appointmentHospitalMap.TryGetValue(appointmentId.Value, out var apptHosp))
            return apptHosp;
        if (checkedInByStaffId.HasValue && staffHospitalMap.TryGetValue(checkedInByStaffId.Value, out var staffHosp))
            return staffHosp;
        return null;
    }

    private async Task<long?> EnsureReceptionAccessAndResolveHospitalIdAsync(
        long actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (actorUserId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        var actorUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (actorUser.AccountStatus != AccountStatus.ACTIVE)
            throw new ForbiddenException("Tài khoản người dùng không ở trạng thái hoạt động.");

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
            throw new ForbiddenException("Tài khoản người dùng đang bị tạm khóa.");

        var actorRoleIds = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var actorRoleCodes = await _db.Roles
            .AsNoTracking()
            .Where(r => actorRoleIds.Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync(ct);

        if (!actorRoleCodes.Any(code => AllowedRoleCodes.Contains(code)))
            throw new ForbiddenException("Người dùng không có quyền truy cập phân hệ tiếp đón.");

        var rolePermissionIds = actorRoleIds.Count > 0
            ? await _db.RolePermissions
                .AsNoTracking()
                .Where(rp => actorRoleIds.Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .Distinct()
                .ToListAsync(ct)
            : [];

        if (rolePermissionIds.Count == 0)
            throw new ForbiddenException("Vai trò hiện tại chưa được cấp quyền truy cập phân hệ tiếp đón.");

        var permissionCodes = await _db.Permissions
            .AsNoTracking()
            .Where(p => rolePermissionIds.Contains(p.Id))
            .Select(p => p.Code)
            .ToListAsync(ct);

        if (!permissionCodes.Any(code => AllowedPermissionCodes.Contains(code)))
            throw new ForbiddenException("Vai trò hiện tại chưa được cấp quyền truy cập phân hệ tiếp đón.");

        var isSystemAdmin = actorRoleCodes.Any(code =>
            string.Equals(code, RoleNames.SystemAdmin, StringComparison.OrdinalIgnoreCase));

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is not null &&
            !string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của người dùng không ở trạng thái hoạt động.");
        }

        if (!isSystemAdmin || actorStaff?.HospitalId.HasValue == true)
        {
            if (actorStaff is null || !actorStaff.HospitalId.HasValue)
                throw new ForbiddenException("Tài khoản nhân sự chưa được gán cơ sở y tế.");

            var hospital = await _db.Hospitals
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == actorStaff.HospitalId.Value, ct)
                ?? throw new NotFoundException("Hospital", actorStaff.HospitalId.Value);

            if (hospital.IsActive == false)
                throw new ForbiddenException("Cơ sở y tế của nhân sự hiện đang ngừng hoạt động.");

            return hospital.Id;
        }

        return await _db.Hospitals
            .AsNoTracking()
            .Where(h => h.IsActive != false)
            .OrderBy(h => h.Id)
            .Select(h => (long?)h.Id)
            .FirstOrDefaultAsync(ct) ?? 0L;
    }

    private static bool IsIdentityNumber(string term) =>
        term.Length == 12 && term.All(char.IsAsciiDigit);

    private static string NormalizeTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab))
            return TabAll;

        var key = AppointmentBookingRules.NormalizeStatusKey(tab);
        return key is TabAll or TabHasAppointment or TabCheckedIn or TabNoAppointment
            ? key
            : throw new ValidationException("Tab không hợp lệ. Giá trị hợp lệ: ALL, HAS_APPOINTMENT, CHECKED_IN, NO_APPOINTMENT.");
    }
}
