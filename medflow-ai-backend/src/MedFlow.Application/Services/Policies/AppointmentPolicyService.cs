using System.Text.Json;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Policies;

public sealed class AppointmentPolicyService : IAppointmentPolicyService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditLogger _audit;

    public AppointmentPolicyService(IApplicationDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<AppointmentPolicyDto> GetEffectiveAsync(long hospitalId, CancellationToken ct = default)
    {
        var rows = await LoadRowsAsync(hospitalId, tracking: false, ct);
        var current = SelectCurrent(rows, DateTime.UtcNow);
        return BuildDto(current);
    }

    public async Task<AppointmentPolicyDto> GetForAdminAsync(long userId, CancellationToken ct = default)
    {
        var hospitalId = await ResolveHospitalIdAsync(userId, ct);
        return await GetEffectiveAsync(hospitalId, ct);
    }

    public async Task<AppointmentPolicyDto> UpdateForAdminAsync(
        long userId,
        UpdateAppointmentPolicyRequest request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var hospitalId = await ResolveHospitalIdAsync(userId, ct);

        var bookingWindowDays = ValidateField(
            request.BookingWindowDays, "Số ngày được đặt trước tối đa", 1, 365);
        var bookingLeadHours = ValidateField(
            request.BookingLeadHours, "Số giờ đặt trước tối thiểu", 0, 168);
        var maxActiveBookings = ValidateField(
            request.MaxActiveBookings, "Số lịch hẹn hoạt động tối đa", 1, 20);
        var rescheduleMinimumHours = ValidateField(
            request.RescheduleMinimumHours, "Số giờ tối thiểu trước khi đổi lịch", 0, 168);
        var maxReschedules = ValidateField(
            request.MaxReschedules, "Số lần đổi lịch tối đa", 0, 10);
        var cancelMinimumHours = ValidateField(
            request.CancelMinimumHours, "Số giờ tối thiểu trước khi hủy lịch", 0, 168);
        var noShowGraceMinutes = ValidateField(
            request.NoShowGraceMinutes, "Số phút chờ trước khi tính vắng mặt", 0, 120);
        var noShowMaxCount = ValidateField(
            request.NoShowMaxCount, "Số lần vắng mặt tối đa", 1, 20);
        var noShowLookbackDays = ValidateField(
            request.NoShowLookbackDays, "Số ngày xét vắng mặt", 1, 365);
        var noShowBlockDays = ValidateField(
            request.NoShowBlockDays, "Số ngày khóa đặt lịch", 0, 365);

        if (bookingLeadHours >= bookingWindowDays * 24)
        {
            throw new ValidationException("Thời gian đặt trước tối thiểu phải nhỏ hơn cửa sổ đặt lịch.");
        }

        var now = DateTime.UtcNow;
        var rows = await LoadRowsAsync(hospitalId, tracking: true, ct);
        var current = SelectCurrent(rows, now);

        var desiredEntries = new (string Key, Dictionary<string, int> NewProps, (string Prop, int DefaultValue)[] Defaults)[]
        {
            (
                AppointmentPolicyKeys.BookingWindowDays,
                new Dictionary<string, int> { [AppointmentPolicyKeys.PropMaxDaysAhead] = bookingWindowDays },
                [(AppointmentPolicyKeys.PropMaxDaysAhead, AppointmentPolicyKeys.DefaultBookingWindowDays)]
            ),
            (
                AppointmentPolicyKeys.BookingLeadHours,
                new Dictionary<string, int> { [AppointmentPolicyKeys.PropMinimumHours] = bookingLeadHours },
                [(AppointmentPolicyKeys.PropMinimumHours, AppointmentPolicyKeys.DefaultBookingLeadHours)]
            ),
            (
                AppointmentPolicyKeys.MaxActiveBookings,
                new Dictionary<string, int> { [AppointmentPolicyKeys.PropCount] = maxActiveBookings },
                [(AppointmentPolicyKeys.PropCount, AppointmentPolicyKeys.DefaultMaxActiveBookings)]
            ),
            (
                AppointmentPolicyKeys.RescheduleHours,
                new Dictionary<string, int> { [AppointmentPolicyKeys.PropMinimumHours] = rescheduleMinimumHours },
                [(AppointmentPolicyKeys.PropMinimumHours, AppointmentPolicyKeys.DefaultRescheduleMinimumHours)]
            ),
            (
                AppointmentPolicyKeys.MaxReschedules,
                new Dictionary<string, int> { [AppointmentPolicyKeys.PropCount] = maxReschedules },
                [(AppointmentPolicyKeys.PropCount, AppointmentPolicyKeys.DefaultMaxReschedules)]
            ),
            (
                AppointmentPolicyKeys.CancelHours,
                new Dictionary<string, int> { [AppointmentPolicyKeys.PropMinimumHours] = cancelMinimumHours },
                [(AppointmentPolicyKeys.PropMinimumHours, AppointmentPolicyKeys.DefaultCancelMinimumHours)]
            ),
            (
                AppointmentPolicyKeys.NoShowGraceMinutes,
                new Dictionary<string, int> { [AppointmentPolicyKeys.PropMinutes] = noShowGraceMinutes },
                [(AppointmentPolicyKeys.PropMinutes, AppointmentPolicyKeys.DefaultNoShowGraceMinutes)]
            ),
            (
                AppointmentPolicyKeys.NoShowLimit,
                new Dictionary<string, int>
                {
                    [AppointmentPolicyKeys.PropMaxCount] = noShowMaxCount,
                    [AppointmentPolicyKeys.PropLookbackDays] = noShowLookbackDays,
                    [AppointmentPolicyKeys.PropBlockDays] = noShowBlockDays,
                },
                [
                    (AppointmentPolicyKeys.PropMaxCount, AppointmentPolicyKeys.DefaultNoShowMaxCount),
                    (AppointmentPolicyKeys.PropLookbackDays, AppointmentPolicyKeys.DefaultNoShowLookbackDays),
                    (AppointmentPolicyKeys.PropBlockDays, AppointmentPolicyKeys.DefaultNoShowBlockDays),
                ]
            ),
        };

        var changes = new List<(string Key, string? OldValue, string NewValue, int NewVersion)>();

        foreach (var (key, newProps, defaults) in desiredEntries)
        {
            var hasRow = current.TryGetValue(key, out var currentRow);
            var isChanged = !hasRow;

            if (hasRow)
            {
                foreach (var (prop, defaultVal) in defaults)
                {
                    var currentVal = ReadInt(currentRow, prop, defaultVal);
                    if (currentVal != newProps[prop])
                    {
                        isChanged = true;
                        break;
                    }
                }
            }

            if (!isChanged)
            {
                continue;
            }

            foreach (var row in rows.Where(r => r.PolicyKey == key && IsEffective(r, now)))
            {
                row.EffectiveTo = now;
            }

            var nextVersion = rows
                .Where(r => r.PolicyKey == key)
                .Select(r => r.PolicyVersion ?? 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

            var newJson = JsonSerializer.Serialize(newProps);

            _db.HospitalPolicies.Add(new HospitalPolicy
            {
                HospitalId = hospitalId,
                QueueServicePointId = null,
                PolicyType = AppointmentPolicyKeys.PolicyType,
                PolicyKey = key,
                PolicyValue = newJson,
                PolicyVersion = nextVersion,
                EffectiveFrom = now,
                EffectiveTo = null,
            });

            changes.Add((key, currentRow?.PolicyValue, newJson, nextVersion));
        }

        if (changes.Count > 0)
        {
            _audit.Add(
                userId,
                "APPOINTMENT_POLICY_UPDATE",
                "HOSPITAL_POLICY",
                hospitalId.ToString(),
                new
                {
                    HospitalId = hospitalId,
                    Changes = changes.Select(c => new
                    {
                        c.Key,
                        c.OldValue,
                        c.NewValue,
                        Version = c.NewVersion,
                    }),
                },
                ipAddress);

            await _db.SaveChangesAsync(ct);
        }

        return await GetEffectiveAsync(hospitalId, ct);
    }

    private async Task<long> ResolveHospitalIdAsync(long userId, CancellationToken ct)
    {
        var hospitalId = await _db.Staff
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.Status == "ACTIVE")
            .Select(s => s.HospitalId)
            .FirstOrDefaultAsync(ct);

        if (hospitalId is null)
        {
            throw new ForbiddenException("Tài khoản quản trị chưa được gán bệnh viện.");
        }

        return hospitalId.Value;
    }

    private async Task<List<HospitalPolicy>> LoadRowsAsync(long hospitalId, bool tracking, CancellationToken ct)
    {
        var query = _db.HospitalPolicies
            .Where(p => p.HospitalId == hospitalId
                && p.PolicyType == AppointmentPolicyKeys.PolicyType
                && p.QueueServicePointId == null);

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.ToListAsync(ct);
    }

    private static Dictionary<string, HospitalPolicy> SelectCurrent(IEnumerable<HospitalPolicy> rows, DateTime now)
    {
        return rows
            .Where(r => IsEffective(r, now))
            .GroupBy(r => r.PolicyKey)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.PolicyVersion ?? 0)
                      .ThenByDescending(r => r.Id)
                      .First());
    }

    private static bool IsEffective(HospitalPolicy row, DateTime now)
        => (row.EffectiveFrom is null || row.EffectiveFrom <= now)
            && (row.EffectiveTo is null || row.EffectiveTo > now);

    private static AppointmentPolicyDto BuildDto(Dictionary<string, HospitalPolicy> current)
    {
        current.TryGetValue(AppointmentPolicyKeys.BookingWindowDays, out var bookingWindowRow);
        current.TryGetValue(AppointmentPolicyKeys.BookingLeadHours, out var bookingLeadRow);
        current.TryGetValue(AppointmentPolicyKeys.MaxActiveBookings, out var maxActiveRow);
        current.TryGetValue(AppointmentPolicyKeys.RescheduleHours, out var rescheduleRow);
        current.TryGetValue(AppointmentPolicyKeys.MaxReschedules, out var maxReschedulesRow);
        current.TryGetValue(AppointmentPolicyKeys.CancelHours, out var cancelRow);
        current.TryGetValue(AppointmentPolicyKeys.NoShowGraceMinutes, out var graceRow);
        current.TryGetValue(AppointmentPolicyKeys.NoShowLimit, out var noShowLimitRow);

        var lastUpdatedAt = current.Count == 0
            ? null
            : current.Values.Max(r => r.EffectiveFrom);

        return new AppointmentPolicyDto(
            BookingWindowDays: ReadInt(
                bookingWindowRow,
                AppointmentPolicyKeys.PropMaxDaysAhead,
                AppointmentPolicyKeys.DefaultBookingWindowDays),
            BookingLeadHours: ReadInt(
                bookingLeadRow,
                AppointmentPolicyKeys.PropMinimumHours,
                AppointmentPolicyKeys.DefaultBookingLeadHours),
            MaxActiveBookings: ReadInt(
                maxActiveRow,
                AppointmentPolicyKeys.PropCount,
                AppointmentPolicyKeys.DefaultMaxActiveBookings),
            RescheduleMinimumHours: ReadInt(
                rescheduleRow,
                AppointmentPolicyKeys.PropMinimumHours,
                AppointmentPolicyKeys.DefaultRescheduleMinimumHours),
            MaxReschedules: ReadInt(
                maxReschedulesRow,
                AppointmentPolicyKeys.PropCount,
                AppointmentPolicyKeys.DefaultMaxReschedules),
            CancelMinimumHours: ReadInt(
                cancelRow,
                AppointmentPolicyKeys.PropMinimumHours,
                AppointmentPolicyKeys.DefaultCancelMinimumHours),
            NoShowGraceMinutes: ReadInt(
                graceRow,
                AppointmentPolicyKeys.PropMinutes,
                AppointmentPolicyKeys.DefaultNoShowGraceMinutes),
            NoShowMaxCount: ReadInt(
                noShowLimitRow,
                AppointmentPolicyKeys.PropMaxCount,
                AppointmentPolicyKeys.DefaultNoShowMaxCount),
            NoShowLookbackDays: ReadInt(
                noShowLimitRow,
                AppointmentPolicyKeys.PropLookbackDays,
                AppointmentPolicyKeys.DefaultNoShowLookbackDays),
            NoShowBlockDays: ReadInt(
                noShowLimitRow,
                AppointmentPolicyKeys.PropBlockDays,
                AppointmentPolicyKeys.DefaultNoShowBlockDays),
            LastUpdatedAt: lastUpdatedAt);
    }

    private static int ReadInt(HospitalPolicy? row, string propertyName, int defaultValue)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.PolicyValue))
        {
            return defaultValue;
        }

        try
        {
            using var doc = JsonDocument.Parse(row.PolicyValue);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(propertyName, out var prop)
                && prop.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
            // Fallback to defaultValue when JSON is malformed.
        }

        return defaultValue;
    }

    private static int ValidateField(int? value, string label, int min, int max)
    {
        if (value is null)
        {
            throw new ValidationException($"{label} là bắt buộc.");
        }

        if (value.Value < min || value.Value > max)
        {
            throw new ValidationException($"{label} phải từ {min} đến {max}.");
        }

        return value.Value;
    }
}
