using System.Text.Json;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;

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
        var rows = await HospitalPolicyStore.LoadRowsAsync(
            _db, hospitalId, AppointmentPolicyKeys.PolicyType, tracking: false, ct);
        var current = HospitalPolicyStore.SelectCurrent(rows, DateTime.UtcNow);
        return BuildDto(current);
    }

    public async Task<AppointmentPolicyDto> GetForAdminAsync(long userId, CancellationToken ct = default)
    {
        var hospitalId = await HospitalPolicyStore.ResolveAdminHospitalIdAsync(_db, userId, ct);
        return await GetEffectiveAsync(hospitalId, ct);
    }

    public async Task<AppointmentPolicyDto> UpdateForAdminAsync(
        long userId,
        UpdateAppointmentPolicyRequest request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var hospitalId = await HospitalPolicyStore.ResolveAdminHospitalIdAsync(_db, userId, ct);

        var bookingWindowDays = HospitalPolicyStore.ValidateRange(
            request.BookingWindowDays, "Số ngày được đặt trước tối đa", 1, 365);
        var bookingLeadHours = HospitalPolicyStore.ValidateRange(
            request.BookingLeadHours, "Số giờ đặt trước tối thiểu", 0, 168);
        var maxActiveBookings = HospitalPolicyStore.ValidateRange(
            request.MaxActiveBookings, "Số lịch hẹn hoạt động tối đa", 1, 20);
        var rescheduleMinimumHours = HospitalPolicyStore.ValidateRange(
            request.RescheduleMinimumHours, "Số giờ tối thiểu trước khi đổi lịch", 0, 168);
        var maxReschedules = HospitalPolicyStore.ValidateRange(
            request.MaxReschedules, "Số lần đổi lịch tối đa", 0, 10);
        var cancelMinimumHours = HospitalPolicyStore.ValidateRange(
            request.CancelMinimumHours, "Số giờ tối thiểu trước khi hủy lịch", 0, 168);
        var noShowGraceMinutes = HospitalPolicyStore.ValidateRange(
            request.NoShowGraceMinutes, "Số phút chờ trước khi tính vắng mặt", 0, 120);
        var noShowMaxCount = HospitalPolicyStore.ValidateRange(
            request.NoShowMaxCount, "Số lần vắng mặt tối đa", 1, 20);
        var noShowLookbackDays = HospitalPolicyStore.ValidateRange(
            request.NoShowLookbackDays, "Số ngày xét vắng mặt", 1, 365);
        var noShowBlockDays = HospitalPolicyStore.ValidateRange(
            request.NoShowBlockDays, "Số ngày khóa đặt lịch", 0, 365);

        if (bookingLeadHours >= bookingWindowDays * 24)
        {
            throw new ValidationException("Thời gian đặt trước tối thiểu phải nhỏ hơn cửa sổ đặt lịch.");
        }

        var now = DateTime.UtcNow;
        var rows = await HospitalPolicyStore.LoadRowsAsync(
            _db, hospitalId, AppointmentPolicyKeys.PolicyType, tracking: true, ct);
        var current = HospitalPolicyStore.SelectCurrent(rows, now);

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
                    var currentVal = HospitalPolicyStore.ReadInt(currentRow, prop, defaultVal);
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

            var newJson = JsonSerializer.Serialize(newProps);
            var nextVersion = HospitalPolicyStore.AppendVersion(
                _db, rows, hospitalId, AppointmentPolicyKeys.PolicyType, key, newJson, now);

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
            BookingWindowDays: HospitalPolicyStore.ReadInt(
                bookingWindowRow,
                AppointmentPolicyKeys.PropMaxDaysAhead,
                AppointmentPolicyKeys.DefaultBookingWindowDays),
            BookingLeadHours: HospitalPolicyStore.ReadInt(
                bookingLeadRow,
                AppointmentPolicyKeys.PropMinimumHours,
                AppointmentPolicyKeys.DefaultBookingLeadHours),
            MaxActiveBookings: HospitalPolicyStore.ReadInt(
                maxActiveRow,
                AppointmentPolicyKeys.PropCount,
                AppointmentPolicyKeys.DefaultMaxActiveBookings),
            RescheduleMinimumHours: HospitalPolicyStore.ReadInt(
                rescheduleRow,
                AppointmentPolicyKeys.PropMinimumHours,
                AppointmentPolicyKeys.DefaultRescheduleMinimumHours),
            MaxReschedules: HospitalPolicyStore.ReadInt(
                maxReschedulesRow,
                AppointmentPolicyKeys.PropCount,
                AppointmentPolicyKeys.DefaultMaxReschedules),
            CancelMinimumHours: HospitalPolicyStore.ReadInt(
                cancelRow,
                AppointmentPolicyKeys.PropMinimumHours,
                AppointmentPolicyKeys.DefaultCancelMinimumHours),
            NoShowGraceMinutes: HospitalPolicyStore.ReadInt(
                graceRow,
                AppointmentPolicyKeys.PropMinutes,
                AppointmentPolicyKeys.DefaultNoShowGraceMinutes),
            NoShowMaxCount: HospitalPolicyStore.ReadInt(
                noShowLimitRow,
                AppointmentPolicyKeys.PropMaxCount,
                AppointmentPolicyKeys.DefaultNoShowMaxCount),
            NoShowLookbackDays: HospitalPolicyStore.ReadInt(
                noShowLimitRow,
                AppointmentPolicyKeys.PropLookbackDays,
                AppointmentPolicyKeys.DefaultNoShowLookbackDays),
            NoShowBlockDays: HospitalPolicyStore.ReadInt(
                noShowLimitRow,
                AppointmentPolicyKeys.PropBlockDays,
                AppointmentPolicyKeys.DefaultNoShowBlockDays),
            LastUpdatedAt: lastUpdatedAt);
    }
}
