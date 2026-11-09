using System.Text.Json;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;

namespace MedFlow.Application.Services.Policies;

public sealed class QueuePolicyService : IQueuePolicyService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditLogger _audit;

    public QueuePolicyService(IApplicationDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<QueuePolicyDto> GetEffectiveAsync(long hospitalId, CancellationToken ct = default)
    {
        var rows = await HospitalPolicyStore.LoadRowsAsync(
            _db, hospitalId, QueuePolicyKeys.PolicyType, tracking: false, ct);
        var current = HospitalPolicyStore.SelectCurrent(rows, DateTime.UtcNow);
        return BuildDto(current);
    }

    public async Task<QueuePolicyDto> GetForAdminAsync(long userId, CancellationToken ct = default)
    {
        var hospitalId = await HospitalPolicyStore.ResolveAdminHospitalIdAsync(_db, userId, ct);
        return await GetEffectiveAsync(hospitalId, ct);
    }

    public async Task<QueuePolicyDto> UpdateForAdminAsync(
        long userId,
        UpdateQueuePolicyRequest request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var hospitalId = await HospitalPolicyStore.ResolveAdminHospitalIdAsync(_db, userId, ct);

        var graceMinutes = HospitalPolicyStore.ValidateRange(
            request.GraceMinutes, "Số phút ân hạn", 0, 120);
        var expectedDurationMinutes = HospitalPolicyStore.ValidateRange(
            request.ExpectedDurationMinutes, "Thời lượng khám dự kiến (phút)", 5, 240);
        var safetyBufferMinutes = HospitalPolicyStore.ValidateRange(
            request.SafetyBufferMinutes, "Khoảng đệm an toàn (phút)", 0, 60);

        if (request.WalkinPriorityRatio is null)
        {
            throw new ValidationException("Tỷ lệ ưu tiên/thường là bắt buộc.");
        }

        var priorityWeight = HospitalPolicyStore.ValidateRange(
            request.WalkinPriorityRatio.Priority, "Trọng số nhóm ưu tiên", 1, 10);
        var normalWeight = HospitalPolicyStore.ValidateRange(
            request.WalkinPriorityRatio.Normal, "Trọng số nhóm thường", 1, 10);

        if (request.PublicNumberImmutable is null)
        {
            throw new ValidationException("Tính bất biến của số thứ tự công khai là bắt buộc.");
        }

        if (request.PublicNumberImmutable.Value == false)
        {
            throw new ValidationException("Số thứ tự công khai luôn bất biến sau khi cấp, không thể tắt.");
        }

        if (request.ManualPriorityRequiresVerification is null)
        {
            throw new ValidationException("Yêu cầu xác minh khi gán ưu tiên thủ công là bắt buộc.");
        }

        var publicNumberImmutable = request.PublicNumberImmutable.Value;
        var manualPriorityRequiresVerification = request.ManualPriorityRequiresVerification.Value;

        var now = DateTime.UtcNow;
        var rows = await HospitalPolicyStore.LoadRowsAsync(
            _db, hospitalId, QueuePolicyKeys.PolicyType, tracking: true, ct);
        var current = HospitalPolicyStore.SelectCurrent(rows, now);

        var desiredEntries = new (string Key, string NewJson, Func<HospitalPolicy, bool> IsSame)[]
        {
            (
                QueuePolicyKeys.GraceMinutes,
                JsonSerializer.Serialize(new Dictionary<string, int>
                {
                    [QueuePolicyKeys.PropMinutes] = graceMinutes,
                }),
                row => HospitalPolicyStore.ReadInt(
                    row, QueuePolicyKeys.PropMinutes, QueuePolicyKeys.DefaultGraceMinutes) == graceMinutes
            ),
            (
                QueuePolicyKeys.ExpectedDurationMinutes,
                JsonSerializer.Serialize(new Dictionary<string, int>
                {
                    [QueuePolicyKeys.PropMinutes] = expectedDurationMinutes,
                }),
                row => HospitalPolicyStore.ReadInt(
                    row, QueuePolicyKeys.PropMinutes, QueuePolicyKeys.DefaultExpectedDurationMinutes) == expectedDurationMinutes
            ),
            (
                QueuePolicyKeys.SafetyBufferMinutes,
                JsonSerializer.Serialize(new Dictionary<string, int>
                {
                    [QueuePolicyKeys.PropMinutes] = safetyBufferMinutes,
                }),
                row => HospitalPolicyStore.ReadInt(
                    row, QueuePolicyKeys.PropMinutes, QueuePolicyKeys.DefaultSafetyBufferMinutes) == safetyBufferMinutes
            ),
            (
                QueuePolicyKeys.WalkinPriorityRatio,
                JsonSerializer.Serialize(new Dictionary<string, int>
                {
                    [QueuePolicyKeys.PropPriority] = priorityWeight,
                    [QueuePolicyKeys.PropNormal] = normalWeight,
                }),
                row => HospitalPolicyStore.ReadInt(
                        row, QueuePolicyKeys.PropPriority, QueuePolicyKeys.DefaultPriorityWeight) == priorityWeight
                    && HospitalPolicyStore.ReadInt(
                        row, QueuePolicyKeys.PropNormal, QueuePolicyKeys.DefaultNormalWeight) == normalWeight
            ),
            (
                QueuePolicyKeys.PublicNumberImmutable,
                JsonSerializer.Serialize(new Dictionary<string, bool>
                {
                    [QueuePolicyKeys.PropEnabled] = publicNumberImmutable,
                }),
                row => HospitalPolicyStore.ReadBool(
                    row, QueuePolicyKeys.PropEnabled, QueuePolicyKeys.DefaultPublicNumberImmutable) == publicNumberImmutable
            ),
            (
                QueuePolicyKeys.ManualPriorityRequiresVerification,
                JsonSerializer.Serialize(new Dictionary<string, bool>
                {
                    [QueuePolicyKeys.PropEnabled] = manualPriorityRequiresVerification,
                }),
                row => HospitalPolicyStore.ReadBool(
                    row, QueuePolicyKeys.PropEnabled, QueuePolicyKeys.DefaultManualPriorityRequiresVerification) == manualPriorityRequiresVerification
            ),
        };

        var changes = new List<(string Key, string? OldValue, string NewValue, int NewVersion)>();

        foreach (var (key, newJson, isSame) in desiredEntries)
        {
            var hasRow = current.TryGetValue(key, out var currentRow);
            var isChanged = !hasRow || !isSame(currentRow!);

            if (!isChanged)
            {
                continue;
            }

            var nextVersion = HospitalPolicyStore.AppendVersion(
                _db, rows, hospitalId, QueuePolicyKeys.PolicyType, key, newJson, now);

            changes.Add((key, currentRow?.PolicyValue, newJson, nextVersion));
        }

        if (changes.Count > 0)
        {
            _audit.Add(
                userId,
                "QUEUE_POLICY_UPDATE",
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

    private static QueuePolicyDto BuildDto(Dictionary<string, HospitalPolicy> current)
    {
        current.TryGetValue(QueuePolicyKeys.GraceMinutes, out var graceRow);
        current.TryGetValue(QueuePolicyKeys.ExpectedDurationMinutes, out var expectedRow);
        current.TryGetValue(QueuePolicyKeys.SafetyBufferMinutes, out var bufferRow);
        current.TryGetValue(QueuePolicyKeys.WalkinPriorityRatio, out var ratioRow);
        current.TryGetValue(QueuePolicyKeys.PublicNumberImmutable, out var immutableRow);
        current.TryGetValue(QueuePolicyKeys.ManualPriorityRequiresVerification, out var verificationRow);

        var lastUpdatedAt = current.Count == 0
            ? null
            : current.Values.Max(r => r.EffectiveFrom);

        return new QueuePolicyDto(
            GraceMinutes: HospitalPolicyStore.ReadInt(
                graceRow,
                QueuePolicyKeys.PropMinutes,
                QueuePolicyKeys.DefaultGraceMinutes),
            ExpectedDurationMinutes: HospitalPolicyStore.ReadInt(
                expectedRow,
                QueuePolicyKeys.PropMinutes,
                QueuePolicyKeys.DefaultExpectedDurationMinutes),
            SafetyBufferMinutes: HospitalPolicyStore.ReadInt(
                bufferRow,
                QueuePolicyKeys.PropMinutes,
                QueuePolicyKeys.DefaultSafetyBufferMinutes),
            WalkinPriorityRatio: new QueuePriorityRatioDto(
                Priority: HospitalPolicyStore.ReadInt(
                    ratioRow,
                    QueuePolicyKeys.PropPriority,
                    QueuePolicyKeys.DefaultPriorityWeight),
                Normal: HospitalPolicyStore.ReadInt(
                    ratioRow,
                    QueuePolicyKeys.PropNormal,
                    QueuePolicyKeys.DefaultNormalWeight)),
            PublicNumberImmutable: HospitalPolicyStore.ReadBool(
                immutableRow,
                QueuePolicyKeys.PropEnabled,
                QueuePolicyKeys.DefaultPublicNumberImmutable),
            ManualPriorityRequiresVerification: HospitalPolicyStore.ReadBool(
                verificationRow,
                QueuePolicyKeys.PropEnabled,
                QueuePolicyKeys.DefaultManualPriorityRequiresVerification),
            LastUpdatedAt: lastUpdatedAt);
    }
}
