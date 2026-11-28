using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Diagnostic;

/// <summary>
/// Quy tắc đề xuất thứ tự CLS + đo tải điểm phục vụ (#869, owner chốt 29/11).
/// Thứ tự: nhịn đói trước -&gt; xét nghiệm máu -&gt; chẩn đoán hình ảnh -&gt; thăm dò chức năng;
/// cùng nhóm thì điểm ít người đang chờ trước. Điều kiện phụ thuộc chuyên môn luôn thắng thứ hạng.
/// Tải điểm (D3) = SỐ NGƯỜI ĐANG CHỜ ở điểm, không tính người đang thực hiện.
/// </summary>
public static class DiagnosticRoutingRules
{
    public const string GroupBloodTest = "BLOOD_TEST";
    public const string GroupImaging = "IMAGING";
    public const string GroupFunctional = "FUNCTIONAL";

    private static readonly HashSet<string> WaitingQueueStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "RESERVED",
        "WAITING",
        "READY",
        "CALLED",
    };

    public static int GroupRank(string? diagnosticGroup) =>
        diagnosticGroup?.Trim().ToUpperInvariant() switch
        {
            GroupBloodTest => 0,
            GroupImaging => 1,
            GroupFunctional => 2,
            _ => 3,
        };

    public static string? NormalizeGroup(string? diagnosticGroup)
    {
        var normalized = diagnosticGroup?.Trim().ToUpperInvariant();
        return normalized is GroupBloodTest or GroupImaging or GroupFunctional ? normalized : null;
    }

    /// <summary>
    /// Đếm số người đang chờ tại từng điểm phục vụ CLS trong ngày.
    /// </summary>
    public static async Task<IReadOnlyDictionary<long, int>> LoadWaitingCountsAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<long> servicePointIds,
        DateOnly queueDate,
        CancellationToken ct = default)
    {
        if (servicePointIds.Count == 0)
        {
            return new Dictionary<long, int>();
        }

        try
        {
            var pointIds = servicePointIds.Distinct().ToList();
            var rows = await db.QueueEntries
                .AsNoTracking()
                .Where(q => q.QueueDate == queueDate)
                .Where(q => q.CurrentServicePointId != null || q.ServicePointId != null)
                .Where(q =>
                    (q.CurrentServicePointId != null && pointIds.Contains(q.CurrentServicePointId.Value)) ||
                    (q.ServicePointId != null && pointIds.Contains(q.ServicePointId.Value)))
                .Select(q => new
                {
                    q.CurrentServicePointId,
                    q.ServicePointId,
                    q.Status,
                    q.PresenceStatus,
                })
                .ToListAsync(ct);

            var counts = new Dictionary<long, int>();
            foreach (var row in rows)
            {
                if (!WaitingQueueStatuses.Contains(row.Status ?? string.Empty))
                {
                    continue;
                }

                if (string.Equals(row.PresenceStatus?.Trim(), "LEFT", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var pointId = row.CurrentServicePointId ?? row.ServicePointId;
                if (!pointId.HasValue)
                {
                    continue;
                }

                counts[pointId.Value] = counts.TryGetValue(pointId.Value, out var current) ? current + 1 : 1;
            }

            return counts;
        }
        catch (NotSupportedException)
        {
            // Minimal unit-test DbContext without QueueEntries.
            return new Dictionary<long, int>();
        }
    }

    /// <summary>
    /// Sắp xếp các chỉ định CLS mới theo thứ tự đề xuất. Điều kiện phụ thuộc chuyên môn
    /// (<paramref name="dependenciesByServiceId"/>) được tôn trọng trước thứ hạng tải/nhóm.
    /// </summary>
    public static IReadOnlyList<T> OrderForSequencing<T>(
        IEnumerable<T> items,
        Func<T, long> serviceIdSelector,
        Func<T, Service?> serviceSelector,
        Func<T, int> waitingCountSelector,
        IReadOnlyDictionary<long, HashSet<long>> dependenciesByServiceId)
    {
        var materialized = items.ToList();
        if (materialized.Count <= 1)
        {
            return materialized;
        }

        var comparable = materialized
            .Select(item => new SequencingItem<T>(
                Item: item,
                ServiceId: serviceIdSelector(item),
                Service: serviceSelector(item),
                WaitingCount: Math.Max(0, waitingCountSelector(item))))
            .ToList();

        var inBatch = comparable.Select(x => x.ServiceId).ToHashSet();
        var alreadyEarlier = new HashSet<long>(); // dependencies trỏ tới dịch vụ đã có chỉ định trước đó
        var pending = comparable.ToList();
        var ordered = new List<T>(comparable.Count);

        while (pending.Count > 0)
        {
            var ready = pending
                .Where(candidate => !IsBlocked(candidate.ServiceId))
                .OrderBy(candidate => candidate.Service?.RequiresFasting == true ? 0 : 1)
                .ThenBy(candidate => GroupRank(candidate.Service?.DiagnosticGroup))
                .ThenBy(candidate => candidate.WaitingCount)
                .ThenBy(candidate => candidate.ServiceId)
                .ToList();

            // Chu trình cấu hình: không được treo luồng khám — fallback sắp xếp thuần thứ hạng.
            if (ready.Count == 0)
            {
                ordered.AddRange(pending
                    .OrderBy(candidate => candidate.Service?.RequiresFasting == true ? 0 : 1)
                    .ThenBy(candidate => GroupRank(candidate.Service?.DiagnosticGroup))
                    .ThenBy(candidate => candidate.WaitingCount)
                    .ThenBy(candidate => candidate.ServiceId)
                    .Select(candidate => candidate.Item));
                break;
            }

            var next = ready[0];
            ordered.Add(next.Item);
            pending.Remove(next);
            alreadyEarlier.Add(next.ServiceId);
        }

        return ordered;

        bool IsBlocked(long serviceId)
        {
            if (!dependenciesByServiceId.TryGetValue(serviceId, out var prerequisites) || prerequisites.Count == 0)
            {
                return false;
            }

            foreach (var prerequisite in prerequisites)
            {
                if (prerequisite == serviceId)
                {
                    continue;
                }

                if (alreadyEarlier.Contains(prerequisite))
                {
                    continue;
                }

                // Dịch vụ phụ thuộc không nằm trong đợt tạo mới => đã có chỉ định trước hoặc không áp dụng.
                if (!inBatch.Contains(prerequisite))
                {
                    continue;
                }

                return true;
            }

            return false;
        }
    }

    private sealed record SequencingItem<T>(
        T Item,
        long ServiceId,
        Service? Service,
        int WaitingCount);
}
