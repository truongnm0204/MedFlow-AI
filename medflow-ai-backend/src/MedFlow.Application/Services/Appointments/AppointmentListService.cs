using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Appointments;

public sealed class AppointmentListService : IAppointmentListService
{
    private const int MaxKeywordLength = 100;
    private const int MaxPageSize = 50;

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public AppointmentListService(IApplicationDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<AppointmentListResponse> GetMyAppointmentsAsync(
        long userId,
        GetMyAppointmentsRequest request,
        CancellationToken ct = default)
    {
        if (userId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        ArgumentNullException.ThrowIfNull(request);

        var normalizedTab = ValidateAndNormalizeTab(request.Tab);
        var normalizedKeyword = ValidateAndNormalizeKeyword(request.Keyword);
        var (dateFrom, dateTo) = ValidateAndParseDateRange(request.DateFrom, request.DateTo);

        if (request.Page < 1)
            throw new ValidationException("Số trang phải lớn hơn hoặc bằng 1.");

        if (request.PageSize is < 1 or > MaxPageSize)
            throw new ValidationException("Kích thước trang phải từ 1 đến 50.");

        var targetMemberId = ValidateAndParseMemberId(request.MemberId);

        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var self = await PatientAccessScope.LoadActiveSelfAsync(_db, userId, ct);

        var familyLinks = await _db.PatientFamilyLinks
            .AsNoTracking()
            .Include(l => l.MemberPatient)
            .WhereActiveMemberOf(self.Id, nowLocal)
            .OrderBy(l => l.MemberPatientId)
            .ToListAsync(ct);

        var scope = AppointmentReadModel.BuildScopeMembers(self, familyLinks, today);
        var scopeById = scope.ToDictionary(m => m.Patient.Id);

        if (targetMemberId.HasValue && !scopeById.ContainsKey(targetMemberId.Value))
            throw new ForbiddenException("Bạn không có quyền xem lịch hẹn của hồ sơ này.");

        var scopeIds = scope.Select(m => m.Patient.Id).ToList();

        var appointments = await _db.Appointments
            .AsNoTracking()
            .IncludeDisplayGraph()
            .Where(a =>
                a.PatientId != null &&
                scopeIds.Contains(a.PatientId.Value) &&
                a.Slot != null &&
                a.Slot.StartsAt != null &&
                a.Slot.EndsAt != null)
            .ToListAsync(ct);

        var related = await AppointmentReadModel.LoadRelatedAsync(_db, appointments.Select(a => a.Id).ToList(), ct);

        var mappedItems = new List<MappedAppointment>(appointments.Count);
        foreach (var appointment in appointments)
        {
            var patientId = appointment.PatientId!.Value;
            if (!scopeById.TryGetValue(patientId, out var scopeMember))
                continue;

            related.VisitsByAppointmentId.TryGetValue(appointment.Id, out var visit);
            related.QueueByAppointmentId.TryGetValue(appointment.Id, out var queueEntry);
            Payment? paid = null;
            if (visit is not null)
                related.PaidByVisitId.TryGetValue(visit.Id, out paid);

            var start = appointment.Slot!.StartsAt!.Value;
            var dto = AppointmentReadModel.MapItem(
                appointment,
                scopeMember,
                visit,
                queueEntry,
                paid,
                nowLocal,
                today);

            mappedItems.Add(new MappedAppointment(
                appointment.Id,
                patientId,
                start,
                DateOnly.FromDateTime(start),
                dto));
        }

        var countByPatientId = mappedItems
            .GroupBy(x => x.PatientId)
            .ToDictionary(g => g.Key, g => g.Count());

        var vi = CultureInfo.InvariantCulture;
        var memberCounts = scope
            .Select(m => new AppointmentMemberCountDto(
                Id: m.Patient.Id.ToString(vi),
                PatientCode: m.Patient.PatientCode,
                FullName: m.Patient.FullName,
                RelationshipLabel: m.RelationshipLabel,
                AgeLabel: m.AgeLabel,
                AppointmentCount: countByPatientId.GetValueOrDefault(m.Patient.Id, 0)))
            .ToList();

        var totalAllMembersCount = memberCounts.Sum(m => m.AppointmentCount);

        IEnumerable<MappedAppointment> filtered = mappedItems;

        if (targetMemberId.HasValue)
            filtered = filtered.Where(x => x.PatientId == targetMemberId.Value);

        if (!string.IsNullOrEmpty(normalizedKeyword))
            filtered = filtered.Where(x => MatchesKeyword(x.Dto, normalizedKeyword));

        if (dateFrom.HasValue)
            filtered = filtered.Where(x => x.ScheduledDateOnly >= dateFrom.Value);

        if (dateTo.HasValue)
            filtered = filtered.Where(x => x.ScheduledDateOnly <= dateTo.Value);

        var preTabList = filtered.ToList();

        var tabCounts = new AppointmentTabCountsDto(
            All: preTabList.Count,
            Upcoming: preTabList.Count(x => AppointmentReadModel.IsUpcomingStatus(x.Dto.Status)),
            Completed: preTabList.Count(x => x.Dto.Status == "COMPLETED"),
            Cancelled: preTabList.Count(x => AppointmentReadModel.IsCancelledStatus(x.Dto.Status)));

        IEnumerable<MappedAppointment> tabFiltered = normalizedTab switch
        {
            "UPCOMING" => preTabList.Where(x => AppointmentReadModel.IsUpcomingStatus(x.Dto.Status)),
            "COMPLETED" => preTabList.Where(x => x.Dto.Status == "COMPLETED"),
            "CANCELLED" => preTabList.Where(x => AppointmentReadModel.IsCancelledStatus(x.Dto.Status)),
            _ => preTabList
        };

        var sorted = normalizedTab == "UPCOMING"
            ? tabFiltered.OrderBy(x => x.StartsAt).ThenBy(x => x.AppointmentId).ToList()
            : tabFiltered.OrderByDescending(x => x.StartsAt).ThenByDescending(x => x.AppointmentId).ToList();

        var total = sorted.Count;
        var totalPages = total == 0 ? 1 : (int)Math.Ceiling((double)total / request.PageSize);

        var pagedItems = sorted
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => x.Dto)
            .ToList();

        return new AppointmentListResponse(
            Items: pagedItems,
            Total: total,
            Page: request.Page,
            PageSize: request.PageSize,
            TotalPages: totalPages,
            TabCounts: tabCounts,
            MemberCounts: memberCounts,
            TotalAllMembersCount: totalAllMembersCount);
    }


    private static string ValidateAndNormalizeTab(string? rawTab)
    {
        if (string.IsNullOrWhiteSpace(rawTab))
            return "ALL";

        var upper = rawTab.Trim().ToUpperInvariant();
        return upper switch
        {
            "ALL" or "UPCOMING" or "COMPLETED" or "CANCELLED" => upper,
            _ => throw new ValidationException("Tab trạng thái không hợp lệ.")
        };
    }

    private static string? ValidateAndNormalizeKeyword(string? rawKeyword)
    {
        if (string.IsNullOrWhiteSpace(rawKeyword))
            return null;

        var trimmed = rawKeyword.Trim();
        if (trimmed.Length > MaxKeywordLength)
            throw new ValidationException("Từ khóa tìm kiếm không được vượt quá 100 ký tự.");

        if (trimmed.StartsWith('#'))
            trimmed = trimmed[1..].TrimStart();

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static (DateOnly? DateFrom, DateOnly? DateTo) ValidateAndParseDateRange(
        string? rawDateFrom,
        string? rawDateTo)
    {
        DateOnly? dateFrom = null;
        DateOnly? dateTo = null;

        if (!string.IsNullOrWhiteSpace(rawDateFrom))
        {
            if (!DateOnly.TryParseExact(
                    rawDateFrom.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedFrom))
            {
                throw new ValidationException("Ngày lọc không hợp lệ (định dạng yyyy-MM-dd).");
            }

            dateFrom = parsedFrom;
        }

        if (!string.IsNullOrWhiteSpace(rawDateTo))
        {
            if (!DateOnly.TryParseExact(
                    rawDateTo.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedTo))
            {
                throw new ValidationException("Ngày lọc không hợp lệ (định dạng yyyy-MM-dd).");
            }

            dateTo = parsedTo;
        }

        if (dateFrom.HasValue && dateTo.HasValue && dateFrom.Value > dateTo.Value)
            throw new ValidationException("Ngày bắt đầu không được sau ngày kết thúc.");

        return (dateFrom, dateTo);
    }

    private static long? ValidateAndParseMemberId(string? rawMemberId)
    {
        if (string.IsNullOrWhiteSpace(rawMemberId))
            return null;

        var trimmed = rawMemberId.Trim();
        if (string.Equals(trimmed, "ALL", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var memberId) || memberId <= 0)
            throw new ValidationException("Mã hồ sơ thành viên không hợp lệ.");

        return memberId;
    }


    private static bool MatchesKeyword(AppointmentListItemDto dto, string keyword)
        => dto.AppointmentCode.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || dto.DoctorName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || dto.SpecialtyName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || dto.ServiceName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || dto.ServiceCode.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || dto.PatientName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || dto.RoomName.Contains(keyword, StringComparison.OrdinalIgnoreCase);


    private sealed record MappedAppointment(
        long AppointmentId,
        long PatientId,
        DateTime StartsAt,
        DateOnly ScheduledDateOnly,
        AppointmentListItemDto Dto);
}
