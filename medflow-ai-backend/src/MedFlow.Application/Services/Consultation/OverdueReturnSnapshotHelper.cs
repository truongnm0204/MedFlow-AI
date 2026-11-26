using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Consultation;

public static class OverdueReturnSnapshotHelper
{
    public const string OverdueDoctorApprovalRequiredMessage =
        "[OVERDUE_RETURN_DOCTOR_APPROVAL_REQUIRED] Lượt khám kết luận đã quá thời hạn quy định. Bác sĩ phụ trách cần xác nhận giá trị chuyên môn của các kết quả cận lâm sàng hiện tại trước khi xác nhận chẩn đoán, kê đơn phát hành hoặc hoàn tất lượt khám.";

    public const string SnapshotMismatchMessage =
        "[OVERDUE_RESULT_SNAPSHOT_MISMATCH] Tập kết quả hoặc chỉ định cận lâm sàng đã thay đổi kể từ lần hiển thị gần nhất. Vui lòng tải lại và xem xét kết quả mới trước khi xác nhận.";

    public static bool IsValidSnapshotHex(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var trimmed = candidate.Trim();
        if (trimmed.Length != 64)
        {
            return false;
        }

        foreach (var ch in trimmed)
        {
            var isHex = (ch >= '0' && ch <= '9') ||
                        (ch >= 'a' && ch <= 'f') ||
                        (ch >= 'A' && ch <= 'F');
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Computes a deterministic SHA-256 hex digest (<c>v2</c>) from pre-loaded entities so read endpoints
    /// (<c>GetWorkspaceAsync</c>, <c>GetDiagnosticResultsAsync</c>) return a <c>CurrentResultSnapshot</c>
    /// guaranteed to match the exact orders, results, and result versions serialized in the same response.
    /// </summary>
    public static string ComputeSnapshotHash(
        Visit visit,
        IEnumerable<DiagnosticOrder> orders,
        IEnumerable<DiagnosticResult> results,
        IEnumerable<DiagnosticResultVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(versions);

        var nonCancelledOrders = orders
            .Where(o => AppointmentBookingRules.NormalizeStatusKey(o.Status) != "CANCELLED")
            .OrderBy(o => o.Id)
            .ToList();

        var resultsByOrderId = results
            .Where(r => r.DiagnosticOrderId.HasValue)
            .GroupBy(r => r.DiagnosticOrderId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(r =>
                        r.FinalizedAt.HasValue ||
                        AppointmentBookingRules.NormalizeStatusKey(r.Status) is "FINALIZED" or "VERIFIED"
                            ? 1
                            : 0)
                    .ThenByDescending(r => r.FinalizedAt ?? DateTime.MinValue)
                    .ThenByDescending(r => r.Id)
                    .First());

        var latestVersionByResultId = versions
            .Where(v => v.DiagnosticResultId.HasValue)
            .GroupBy(v => v.DiagnosticResultId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(v => v.VersionNo ?? 0)
                    .ThenByDescending(v => v.Id)
                    .First());

        var entries = new List<string>(nonCancelledOrders.Count);
        foreach (var order in nonCancelledOrders)
        {
            resultsByOrderId.TryGetValue(order.Id, out var res);
            DiagnosticResultVersion? ver = null;
            if (res is not null)
            {
                latestVersionByResultId.TryGetValue(res.Id, out ver);
            }

            var contentCanonical = string.Concat(
                ver?.ResultText?.Trim() ?? string.Empty,
                "\n",
                ver?.Conclusion?.Trim() ?? string.Empty,
                "\n",
                ver?.ReportFileUrl?.Trim() ?? string.Empty);
            var contentDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contentCanonical)))
                .ToLowerInvariant();

            var orderStatus = AppointmentBookingRules.NormalizeStatusKey(order.Status);
            var selectionStatus = AppointmentBookingRules.NormalizeStatusKey(order.SelectionStatus);
            var reqFlag = order.RequiredForConclusion == true ? "1" : "0";
            var resultId = res?.Id ?? 0;
            var resultStatus = AppointmentBookingRules.NormalizeStatusKey(res?.Status);
            var versionId = ver?.Id ?? 0;
            var versionNo = ver?.VersionNo ?? 0;
            var isFinalizedFlag = ver?.IsFinalized == true ? "1" : "0";
            var verifiedByStaffId = ver?.VerifiedByStaffId ?? 0;

            entries.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{order.Id}:{orderStatus}:{selectionStatus}:{reqFlag}:{resultId}:{resultStatus}:{versionId}:{versionNo}:{isFinalizedFlag}:{verifiedByStaffId}:{contentDigest}"));
        }

        var canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"v2|visitId={visit.Id}|assignedDoctorId={visit.AssignedDoctorId ?? 0}|orders={string.Join(";", entries)}");

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Queries live <see cref="DiagnosticOrder"/>, <see cref="DiagnosticResult"/>, and <see cref="DiagnosticResultVersion"/>
    /// rows for <paramref name="visit"/> and computes the <c>v2</c> SHA-256 snapshot digest.
    /// Fails closed (never swallows <see cref="NotSupportedException"/> or returns an empty-order digest on query failure).
    /// </summary>
    public static async Task<string> ComputeSnapshotHashAsync(
        IApplicationDbContext db,
        Visit visit,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(visit);

        var orders = await db.DiagnosticOrders
            .AsNoTracking()
            .Where(o => o.VisitId == visit.Id)
            .OrderBy(o => o.Id)
            .ToListAsync(ct);

        var nonCancelledOrders = orders
            .Where(o => AppointmentBookingRules.NormalizeStatusKey(o.Status) != "CANCELLED")
            .OrderBy(o => o.Id)
            .ToList();

        var orderIds = nonCancelledOrders.Select(o => o.Id).ToList();
        var results = orderIds.Count == 0
            ? []
            : await db.DiagnosticResults
                .AsNoTracking()
                .Where(r => r.DiagnosticOrderId.HasValue && orderIds.Contains(r.DiagnosticOrderId.Value))
                .ToListAsync(ct);

        var resultIds = results.Select(r => r.Id).Distinct().ToList();
        var versions = resultIds.Count == 0
            ? []
            : await db.DiagnosticResultVersions
                .AsNoTracking()
                .Where(v => v.DiagnosticResultId.HasValue && resultIds.Contains(v.DiagnosticResultId.Value))
                .ToListAsync(ct);

        return ComputeSnapshotHash(visit, nonCancelledOrders, results, versions);
    }

    public static bool IsApprovedForComputedSnapshot(
        Visit visit,
        long actingDoctorId,
        string currentSnapshotHash)
    {
        if (!visit.OverdueReturnApprovedByDoctorId.HasValue ||
            !visit.OverdueReturnApprovedAt.HasValue ||
            !IsValidSnapshotHex(visit.OverdueReturnResultSnapshot))
        {
            return false;
        }

        if (visit.OverdueReturnApprovedByDoctorId.Value != actingDoctorId)
        {
            return false;
        }

        if (visit.AssignedDoctorId.HasValue && visit.AssignedDoctorId.Value != actingDoctorId)
        {
            return false;
        }

        return string.Equals(
            visit.OverdueReturnResultSnapshot!.Trim(),
            currentSnapshotHash,
            StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<bool> IsApprovedForCurrentSnapshotAsync(
        IApplicationDbContext db,
        Visit visit,
        long actingDoctorId,
        CancellationToken ct)
    {
        var currentHash = await ComputeSnapshotHashAsync(db, visit, ct);
        return IsApprovedForComputedSnapshot(visit, actingDoctorId, currentHash);
    }
}
