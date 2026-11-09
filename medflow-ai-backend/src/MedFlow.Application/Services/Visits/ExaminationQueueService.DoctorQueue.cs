using System.Globalization;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Services.Visits.Queueing;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits;

public sealed partial class ExaminationQueueService
{
    private const string DoctorRoleCode = "DOCTOR";

    public async Task<DoctorAssignedQueueResponseDto> GetDoctorAssignedQueueAsync(
        long actorUserId,
        GetDoctorAssignedQueueRequest? request = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var explicitQueueDate = ParseAndValidateQueueDate(request?.QueueDate);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var (hospital, doctor) = await EnsureDoctorQueueAccessAsync(actorUserId, nowUtc, ct);
        var board = await BuildQueueBoardForHospitalAsync(hospital, explicitQueueDate, ct);

        // Authorization layer 2: only service points owned by the acting doctor.
        var servicePoints = board.Buckets
            .Where(b => b.Bucket.DoctorId == doctor.Id)
            .Select(BuildDoctorServicePoint)
            .ToList();

        var allEntries = servicePoints
            .SelectMany(sp => sp.Entries)
            .ToList();

        return new DoctorAssignedQueueResponseDto(
            QueueDate: board.EffectiveQueueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            FacilityName: hospital.Name,
            ShiftLabel: ResolveShiftLabel(board.NowLocal),
            GeneratedAtUtc: board.NowUtc,
            DoctorName: FormatDoctorName(doctor),
            Stats: BuildDoctorQueueStats(allEntries),
            ServicePoints: servicePoints);
    }

    private static DoctorAssignedServicePointDto BuildDoctorServicePoint(QueueBoardBucket boardBucket)
    {
        var bucket = boardBucket.Bucket;
        var entries = boardBucket.Entries
            .Select(e => e.Dto)
            .ToList();

        var currentServing = entries.FirstOrDefault(e => e.Status == "IN_PROGRESS")
            ?? entries.FirstOrDefault(e => e.Status == "CALLED");
        var nextEligible = entries.FirstOrDefault(e => e.QueueGroup == QueueGroups.Eligible);

        return new DoctorAssignedServicePointDto(
            ServicePointId: bucket.ServicePointId,
            ServicePointName: bucket.ServicePointName,
            RoomCode: bucket.RoomCode,
            RoomName: bucket.RoomName,
            DepartmentName: bucket.DepartmentName,
            IsActive: bucket.IsActive,
            CurrentServingTicketNo: currentServing?.TicketNo,
            NextEligibleTicketNo: nextEligible?.TicketNo,
            Stats: BuildDoctorQueueStats(entries),
            Entries: entries);
    }

    private static DoctorAssignedQueueStatsDto BuildDoctorQueueStats(IReadOnlyCollection<ExaminationQueueEntryDto> entries)
        => new(
            TotalEntries: entries.Count,
            InProgressCount: entries.Count(e => e.QueueGroup == QueueGroups.InProgress),
            CalledCount: entries.Count(e => e.QueueGroup == QueueGroups.Called),
            EligibleCount: entries.Count(e => e.QueueGroup == QueueGroups.Eligible),
            NotEligibleCount: entries.Count(e => e.QueueGroup == QueueGroups.NotEligible),
            SkippedCount: entries.Count(e => e.QueueGroup == QueueGroups.Skipped),
            CompletedCount: entries.Count(e => e.QueueGroup == QueueGroups.Completed));

    private async Task<(Hospital Hospital, Doctor Doctor)> EnsureDoctorQueueAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var actorUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (actorUser.AccountStatus != AccountStatus.ACTIVE)
        {
            throw new ForbiddenException("Tài khoản bác sĩ không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản bác sĩ đang bị tạm khóa.");
        }

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

        if (!actorRoleCodes.Any(c => string.Equals(c, DoctorRoleCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Người dùng không có quyền xem hàng đợi được phân công của Bác sĩ.");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is null || !actorStaff.HospitalId.HasValue)
        {
            throw new ForbiddenException("Tài khoản bác sĩ chưa được gán cơ sở y tế.");
        }

        if (!string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của bác sĩ đã ngừng hoạt động.");
        }

        var actorDoctor = await _db.Doctors
            .AsNoTracking()
            .Include(d => d.Staff)
            .FirstOrDefaultAsync(d => d.StaffId == actorStaff.Id, ct)
            ?? throw new ForbiddenException("Tài khoản chưa được liên kết với hồ sơ bác sĩ lâm sàng.");

        if (!actorDoctor.IsActive)
        {
            throw new ForbiddenException("Hồ sơ hành nghề của bác sĩ hiện đang tạm ngừng hoạt động.");
        }

        if (actorDoctor.HospitalId.HasValue &&
            actorDoctor.HospitalId.Value != actorStaff.HospitalId.Value)
        {
            throw new ForbiddenException("Hồ sơ bác sĩ không thuộc cùng cơ sở y tế với hồ sơ nhân sự.");
        }

        var hospitalId = actorDoctor.HospitalId ?? actorStaff.HospitalId.Value;

        var hospital = await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == hospitalId, ct)
            ?? throw new NotFoundException("Hospital", hospitalId);

        if (hospital.IsActive == false)
        {
            throw new ForbiddenException("Cơ sở y tế được gán cho bác sĩ hiện đang ngừng hoạt động.");
        }

        return (hospital, actorDoctor);
    }
}
