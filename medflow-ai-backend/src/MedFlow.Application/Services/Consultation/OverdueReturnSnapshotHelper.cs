using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Consultation;

public static class OverdueReturnSnapshotHelper
{
    public const string OverdueDoctorApprovalRequiredMessage =
        "[OVERDUE_RETURN_DOCTOR_APPROVAL_REQUIRED] Lượt khám kết luận đã quá thời hạn quy định. Bác sĩ phụ trách cần xác nhận giá trị chuyên môn của các kết quả cận lâm sàng hiện hành trước khi xác nhận chẩn đoán, kê đơn phát hành hoặc hoàn tất lượt khám.";

    public static async Task<string> ComputeSnapshotHashAsync(
        IApplicationDbContext db,
        Visit visit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(visit);

        List<DiagnosticOrder> orders;
        try
        {
            orders = await db.DiagnosticOrders
                .AsNoTracking()
                .Where(o => o.VisitId == visit.Id)
                .OrderBy(o => o.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            orders = [];
        }

        var nonCancelledOrders = orders
            .Where(o => AppointmentBookingRules.NormalizeStatusKey(o.Status) != "CANCELLED")
            .OrderBy(o => o.Id)
            .ToList();

        var orderIds = nonCancelledOrders.Select(o => o.Id).ToList();

        List<DiagnosticResult> results = [];
        List<DiagnosticResultVersion> versions = [];

        if (orderIds.Count > 0)
        {
            try
            {
                results = await db.DiagnosticResults
                    .AsNoTracking()
                    .Where(r => r.DiagnosticOrderId.HasValue && orderIds.Contains(r.DiagnosticOrderId.Value))
                    .OrderByDescending(r => r.Id)
                    .ToListAsync(ct);

                var resultIds = results.Select(r => r.Id).Distinct().ToList();
                if (resultIds.Count > 0)
                {
                    versions = await db.DiagnosticResultVersions
                        .AsNoTracking()
                        .Where(v => v.DiagnosticResultId.HasValue && resultIds.Contains(v.DiagnosticResultId.Value))
                        .OrderByDescending(v => v.VersionNo)
                        .ThenByDescending(v => v.Id)
                        .ToListAsync(ct);
                }
            }
            catch (NotSupportedException)
            {
                results = [];
                versions = [];
            }
        }

        var latestResultByOrderId = results
            .Where(r => r.DiagnosticOrderId.HasValue && AppointmentBookingRules.NormalizeStatusKey(r.Status) != "CANCELLED")
            .GroupBy(r => r.DiagnosticOrderId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Id).First());

        var latestVersionByResultId = versions
            .Where(v => v.DiagnosticResultId.HasValue)
            .GroupBy(v => v.DiagnosticResultId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(v => v.VersionNo ?? 0).ThenByDescending(v => v.Id).First());

        var sb = new StringBuilder(256);
        sb.Append("v1|assignedDoctorId=");
        sb.Append((visit.AssignedDoctorId ?? 0).ToString(CultureInfo.InvariantCulture));
        sb.Append("|orders=");

        for (var i = 0; i < nonCancelledOrders.Count; i++)
        {
            if (i > 0)
                sb.Append(';');

            var order = nonCancelledOrders[i];
            latestResultByOrderId.TryGetValue(order.Id, out var res);
            DiagnosticResultVersion? ver = null;
            if (res is not null)
            {
                latestVersionByResultId.TryGetValue(res.Id, out ver);
            }

            var reqFlag = order.RequiredForConclusion == false ? 0 : 1;
            var ordStatus = AppointmentBookingRules.NormalizeStatusKey(order.Status);
            var selStatus = string.IsNullOrWhiteSpace(order.SelectionStatus)
                ? DiagnosticSelectionStatuses.Accepted
                : order.SelectionStatus.Trim().ToUpperInvariant();
            var resId = res?.Id ?? 0;
            var resStatus = res is not null ? AppointmentBookingRules.NormalizeStatusKey(res.Status) : "NONE";
            var verNo = ver?.VersionNo ?? 0;
            var isFin = (ver?.IsFinalized == true || resStatus is "FINALIZED" or "VERIFIED" or "AMENDED") ? 1 : 0;
            var verifiedBy = ver?.VerifiedByStaffId ?? res?.FinalizedByStaffId ?? 0;

            sb.Append(order.Id.ToString(CultureInfo.InvariantCulture)).Append(':')
              .Append((order.ServiceId ?? 0).ToString(CultureInfo.InvariantCulture)).Append(':')
              .Append(reqFlag.ToString(CultureInfo.InvariantCulture)).Append(':')
              .Append(ordStatus).Append(':')
              .Append(selStatus).Append(':')
              .Append(resId.ToString(CultureInfo.InvariantCulture)).Append(':')
              .Append(resStatus).Append(':')
              .Append(verNo.ToString(CultureInfo.InvariantCulture)).Append(':')
              .Append(isFin.ToString(CultureInfo.InvariantCulture)).Append(':')
              .Append(verifiedBy.ToString(CultureInfo.InvariantCulture));
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static async Task<bool> IsApprovedForCurrentSnapshotAsync(
        IApplicationDbContext db,
        Visit visit,
        long doctorId,
        CancellationToken ct = default)
    {
        if (visit.OverdueReturnApprovedByDoctorId != doctorId ||
            !visit.OverdueReturnApprovedAt.HasValue ||
            string.IsNullOrWhiteSpace(visit.OverdueReturnResultSnapshot))
        {
            return false;
        }

        var currentHash = await ComputeSnapshotHashAsync(db, visit, ct);
        return string.Equals(visit.OverdueReturnResultSnapshot.Trim(), currentHash, StringComparison.OrdinalIgnoreCase);
    }

    public static void InvalidateSnapshot(Visit? visit)
    {
        if (visit is null)
            return;

        visit.OverdueReturnResultSnapshot = null;
    }
}
