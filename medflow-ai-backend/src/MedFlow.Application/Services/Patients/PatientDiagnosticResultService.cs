using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Doctors;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Patients;

public sealed class PatientDiagnosticResultService : IPatientDiagnosticResultService
{
    private const int MaxIdentifierLength = 40;
    private const int MaxPageSize = 50;
    private const string PublishedStatus = "PUBLISHED";
    private const string PendingStatus = "PENDING_RELEASE";
    private const string NotFoundMessage = "Không tìm thấy kết quả cận lâm sàng.";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public PatientDiagnosticResultService(IApplicationDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<PatientDiagnosticResultListResponseDto> GetMyResultsAsync(
        long actorUserId,
        GetPatientDiagnosticResultsRequest? request,
        CancellationToken ct = default)
    {
        EnsureActor(actorUserId);
        var paging = NormalizeListRequest(request);
        var scope = await LoadScopeAsync(actorUserId, ct);

        if (paging.PatientId is long requestedPatientId)
        {
            scope = scope.Where(member => member.Patient.Id == requestedPatientId).ToList();
            if (scope.Count == 0)
                throw new ForbiddenException("Bạn không có quyền xem kết quả cận lâm sàng của hồ sơ này.");
        }

        var bundles = await LoadScopedBundlesAsync(scope, paging.VisitId, paging.VisitCode, ct);
        var visible = new List<VisibleResult>(bundles.Count);
        foreach (var bundle in bundles)
        {
            if (TryRead(bundle, scope) is { } visibleResult)
                visible.Add(visibleResult);
        }

        var lookups = await LoadLookupsAsync(visible, ct);
        var mapped = visible
            .Select(item => MapListItem(item, lookups))
            .OrderByDescending(item => item.IsPublishedToPatient)
            .ThenByDescending(item => item.SignedAt)
            .ThenByDescending(item => item.OrderId)
            .ToList();

        var pageItems = mapped
            .Skip((paging.Page - 1) * paging.PageSize)
            .Take(paging.PageSize)
            .ToList();

        return new PatientDiagnosticResultListResponseDto(
            pageItems,
            paging.Page,
            paging.PageSize,
            mapped.Count);
    }

    public async Task<PatientDiagnosticResultDetailDto> GetMyResultAsync(
        long actorUserId,
        string? idOrCode,
        CancellationToken ct = default)
    {
        EnsureActor(actorUserId);
        var identifier = ParseOrderIdentifier(idOrCode);
        var scope = await LoadScopeAsync(actorUserId, ct);

        var order = identifier.Id is long orderId
            ? await _db.DiagnosticOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct)
            : await _db.DiagnosticOrders.AsNoTracking().FirstOrDefaultAsync(
                o => o.OrderCode != null && o.OrderCode.ToUpper() == identifier.Code,
                ct);

        if (order?.VisitId is null)
            throw new NotFoundException(NotFoundMessage);

        var visit = await _db.Visits.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == order.VisitId.Value, ct);
        if (visit?.PatientId is null)
            throw new NotFoundException(NotFoundMessage);

        var member = scope.FirstOrDefault(item => item.Patient.Id == visit.PatientId.Value);
        if (member is null)
            throw new ForbiddenException("Bạn không có quyền xem kết quả cận lâm sàng này.");

        var result = await _db.DiagnosticResults.AsNoTracking()
            .Where(r => r.DiagnosticOrderId == order.Id)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);
        if (result is null)
            throw new NotFoundException(NotFoundMessage);

        var versions = await _db.DiagnosticResultVersions.AsNoTracking()
            .Where(v => v.DiagnosticResultId == result.Id)
            .ToListAsync(ct);
        var queue = order.QueueEntryId is long queueId
            ? await _db.QueueEntries.AsNoTracking().FirstOrDefaultAsync(q => q.Id == queueId, ct)
            : null;

        var bundle = new OrderBundle(order, visit, result, versions, queue);
        if (TryRead(bundle) is not { } visible)
            throw new NotFoundException(NotFoundMessage);

        var lookups = await LoadLookupsAsync([visible], ct);
        return MapDetail(visible, member, lookups);
    }

    private async Task<List<AppointmentReadModel.ScopeMember>> LoadScopeAsync(
        long actorUserId,
        CancellationToken ct)
    {
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var owned = await AppointmentBookingRules.LoadOwnedPatientsAsync(_db, actorUserId, nowLocal, ct);
        return AppointmentReadModel.BuildScopeMembers(
            owned.Self,
            owned.FamilyLinks,
            DateOnly.FromDateTime(nowLocal));
    }

    private async Task<List<OrderBundle>> LoadScopedBundlesAsync(
        IReadOnlyList<AppointmentReadModel.ScopeMember> scope,
        long? visitId,
        string? visitCode,
        CancellationToken ct)
    {
        var patientIds = scope.Select(member => member.Patient.Id).Distinct().ToList();
        if (patientIds.Count == 0)
            return [];

        var visitsQuery = _db.Visits.AsNoTracking()
            .Where(v => v.PatientId != null && patientIds.Contains(v.PatientId.Value));
        if (visitId is long requestedVisitId)
            visitsQuery = visitsQuery.Where(v => v.Id == requestedVisitId);
        if (visitCode is not null)
            visitsQuery = visitsQuery.Where(v => v.VisitCode != null && v.VisitCode.ToUpper() == visitCode);

        var visits = await visitsQuery.ToListAsync(ct);
        if (visits.Count == 0)
            return [];

        var visitsById = visits.ToDictionary(v => v.Id);
        var visitIds = visitsById.Keys.ToList();
        var orders = await _db.DiagnosticOrders.AsNoTracking()
            .Where(o => o.VisitId != null && visitIds.Contains(o.VisitId.Value))
            .ToListAsync(ct);
        if (orders.Count == 0)
            return [];

        var orderIds = orders.Select(o => o.Id).ToList();
        var results = await _db.DiagnosticResults.AsNoTracking()
            .Where(r => r.DiagnosticOrderId != null && orderIds.Contains(r.DiagnosticOrderId.Value))
            .ToListAsync(ct);
        var resultByOrderId = results
            .GroupBy(r => r.DiagnosticOrderId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Id).First());
        if (resultByOrderId.Count == 0)
            return [];

        var resultIds = resultByOrderId.Values.Select(r => r.Id).ToList();
        var versions = await _db.DiagnosticResultVersions.AsNoTracking()
            .Where(v => v.DiagnosticResultId != null && resultIds.Contains(v.DiagnosticResultId.Value))
            .ToListAsync(ct);
        var versionsByResultId = versions
            .GroupBy(v => v.DiagnosticResultId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DiagnosticResultVersion>)g.ToList());

        var queueIds = orders
            .Where(o => o.QueueEntryId.HasValue)
            .Select(o => o.QueueEntryId!.Value)
            .Distinct()
            .ToList();
        var queuesById = queueIds.Count == 0
            ? new Dictionary<long, QueueEntry>()
            : await _db.QueueEntries.AsNoTracking()
                .Where(q => queueIds.Contains(q.Id))
                .ToDictionaryAsync(q => q.Id, ct);

        var bundles = new List<OrderBundle>(resultByOrderId.Count);
        foreach (var order in orders)
        {
            if (order.VisitId is not long orderVisitId ||
                !visitsById.TryGetValue(orderVisitId, out var visit) ||
                !resultByOrderId.TryGetValue(order.Id, out var result))
            {
                continue;
            }

            versionsByResultId.TryGetValue(result.Id, out var resultVersions);
            QueueEntry? queue = null;
            if (order.QueueEntryId is long queueId)
                queuesById.TryGetValue(queueId, out queue);

            bundles.Add(new OrderBundle(
                order,
                visit,
                result,
                resultVersions ?? [],
                queue));
        }

        return bundles;
    }

    private async Task<DisplayLookups> LoadLookupsAsync(
        IReadOnlyList<VisibleResult> items,
        CancellationToken ct)
    {
        var serviceIds = items
            .Select(item => item.Bundle.Order.ServiceId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var services = serviceIds.Count == 0
            ? new Dictionary<long, Service>()
            : await _db.Services.AsNoTracking()
                .Where(s => serviceIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct);

        var hospitalIds = services.Values
            .Select(s => s.HospitalId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var hospitals = hospitalIds.Count == 0
            ? new Dictionary<long, Hospital>()
            : await _db.Hospitals.AsNoTracking()
                .Where(h => hospitalIds.Contains(h.Id))
                .ToDictionaryAsync(h => h.Id, ct);

        var doctorIds = items
            .Select(item => item.Bundle.Order.OrderingDoctorId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var doctors = doctorIds.Count == 0
            ? new Dictionary<long, Doctor>()
            : await _db.Doctors.AsNoTracking()
                .Where(d => doctorIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, ct);

        var pointIds = items
            .Select(item => item.Bundle.Queue?.CurrentServicePointId ?? item.Bundle.Queue?.ServicePointId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var points = pointIds.Count == 0
            ? new Dictionary<long, QueueServicePoint>()
            : await _db.QueueServicePoints.AsNoTracking()
                .Where(p => pointIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

        var roomIds = points.Values
            .Select(p => p.RoomId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var rooms = roomIds.Count == 0
            ? new Dictionary<long, HospitalRoom>()
            : await _db.HospitalRooms.AsNoTracking()
                .Where(r => roomIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, ct);

        var staffIds = doctors.Values
            .Select(d => d.StaffId)
            .Concat(items.Select(item => item.Content?.VerifiedByStaffId))
            .Concat(items.Select(item => item.Content?.EnteredByStaffId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var staff = staffIds.Count == 0
            ? new Dictionary<long, Staff>()
            : await _db.Staff.AsNoTracking()
                .Where(s => staffIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct);

        return new DisplayLookups(services, hospitals, doctors, staff, points, rooms);
    }

    private static PatientDiagnosticResultListItemDto MapListItem(VisibleResult item, DisplayLookups lookups)
    {
        var place = ResolvePlace(item.Bundle, lookups);
        return new PatientDiagnosticResultListItemDto(
            item.Bundle.Order.Id,
            item.Bundle.Order.OrderCode,
            item.Bundle.Visit.Id,
            item.Bundle.Visit.VisitCode,
            item.Member.Patient.Id,
            item.Member.Patient.PatientCode,
            item.Member.Patient.FullName,
            item.Member.RelationshipLabel,
            ResolveHospitalName(item.Bundle, lookups),
            ResolveServiceName(item.Bundle, lookups),
            ResolveOrderingDoctorName(item.Bundle, lookups),
            place,
            item.IsPublished ? item.SignedAt : null,
            item.IsPublished,
            item.IsPublished ? PublishedStatus : PendingStatus);
    }

    private static PatientDiagnosticResultDetailDto MapDetail(
        VisibleResult item,
        AppointmentReadModel.ScopeMember member,
        DisplayLookups lookups)
    {
        var listItem = MapListItem(item with { Member = member }, lookups);
        string? resultText = null;
        string? conclusion = null;
        string? reportFileUrl = null;
        string? fileName = null;
        string? performingStaffName = null;

        if (item.IsPublished && item.Content is not null)
        {
            resultText = item.Content.ResultText;
            conclusion = item.Content.Conclusion;
            reportFileUrl = item.Content.ReportFileUrl;
            fileName = ExtractFileName(reportFileUrl);
            performingStaffName = ResolveStaffName(
                item.Content.VerifiedByStaffId ?? item.Content.EnteredByStaffId,
                lookups);
        }

        return new PatientDiagnosticResultDetailDto(
            listItem.OrderId,
            listItem.OrderCode,
            listItem.VisitId,
            listItem.VisitCode,
            listItem.PatientId,
            listItem.PatientCode,
            listItem.PatientName,
            member.Patient.Gender,
            member.Patient.DateOfBirth?.Year,
            listItem.RelationshipLabel,
            listItem.HospitalName,
            listItem.ServiceName,
            listItem.OrderingDoctorName,
            performingStaffName,
            listItem.PerformedPlaceName,
            listItem.SignedAt,
            listItem.IsPublishedToPatient,
            listItem.Status,
            resultText,
            conclusion,
            reportFileUrl,
            fileName);
    }

    private static VisibleResult? TryRead(OrderBundle bundle)
    {
        if (IsCancelled(bundle))
            return null;

        var latest = ChooseLatestVersion(bundle.Versions);
        var publishedVersion = ChoosePublishedVersion(bundle.Versions);
        var rowFinalized = IsDiagnosticResultRowFinalized(bundle.Result, latest);
        var isPublished = rowFinalized || publishedVersion is not null;
        var content = isPublished ? publishedVersion ?? latest : null;
        var signedAt = content?.VerifiedAt ?? bundle.Result.FinalizedAt;

        return new VisibleResult(bundle, null!, isPublished, content, signedAt);
    }

    private static VisibleResult? TryRead(OrderBundle bundle, IReadOnlyList<AppointmentReadModel.ScopeMember> scope)
    {
        var visible = TryRead(bundle);
        if (visible is null || bundle.Visit.PatientId is not long patientId)
            return null;

        var member = scope.FirstOrDefault(item => item.Patient.Id == patientId);
        return member is null ? null : visible with { Member = member };
    }

    private static bool IsCancelled(OrderBundle bundle)
    {
        var orderStatus = AppointmentBookingRules.NormalizeStatusKey(bundle.Order.Status);
        var queueStatus = AppointmentBookingRules.NormalizeStatusKey(bundle.Queue?.Status);
        var resultStatus = AppointmentBookingRules.NormalizeStatusKey(bundle.Result.Status);
        return orderStatus == "CANCELLED" || queueStatus == "CANCELLED" || resultStatus == "CANCELLED";
    }

    /// <summary>
    /// Same predicate as <c>DiagnosticResultEntryService.IsDiagnosticResultRowFinalized</c>.
    /// </summary>
    private static bool IsDiagnosticResultRowFinalized(
        DiagnosticResult? result,
        DiagnosticResultVersion? latestVersion)
    {
        if (result is null)
            return false;

        var rawResultStatus = AppointmentBookingRules.NormalizeStatusKey(result.Status);
        return result.FinalizedAt.HasValue ||
               rawResultStatus is "FINALIZED" or "VERIFIED" or "COMPLETED" ||
               latestVersion?.IsFinalized == true ||
               latestVersion?.VerifiedAt.HasValue == true;
    }

    private static DiagnosticResultVersion? ChoosePublishedVersion(
        IReadOnlyList<DiagnosticResultVersion> versions)
        => versions
            .Where(v => v.IsFinalized == true || v.VerifiedAt.HasValue)
            .OrderByDescending(v => v.VersionNo ?? 0)
            .ThenByDescending(v => v.Id)
            .FirstOrDefault();

    private static DiagnosticResultVersion? ChooseLatestVersion(
        IReadOnlyList<DiagnosticResultVersion> versions)
        => versions
            .OrderByDescending(v => v.VersionNo ?? 0)
            .ThenByDescending(v => v.Id)
            .FirstOrDefault();

    private static string? ResolveHospitalName(OrderBundle bundle, DisplayLookups lookups)
    {
        if (bundle.Order.ServiceId is not long serviceId ||
            !lookups.Services.TryGetValue(serviceId, out var service) ||
            service.HospitalId is not long hospitalId ||
            !lookups.Hospitals.TryGetValue(hospitalId, out var hospital))
        {
            return null;
        }

        return BlankToNull(hospital.Name);
    }

    private static string? ResolveServiceName(OrderBundle bundle, DisplayLookups lookups)
    {
        if (bundle.Order.ServiceId is not long serviceId ||
            !lookups.Services.TryGetValue(serviceId, out var service))
        {
            return null;
        }

        return BlankToNull(service.Name);
    }

    private static string? ResolveOrderingDoctorName(OrderBundle bundle, DisplayLookups lookups)
    {
        if (bundle.Order.OrderingDoctorId is not long doctorId ||
            !lookups.Doctors.TryGetValue(doctorId, out var doctor))
        {
            return null;
        }

        lookups.Staff.TryGetValue(doctor.StaffId ?? 0, out var staff);
        var name = DoctorCatalogService.FormatDisplayName(staff?.FullName, doctor.ProfessionalTitle ?? "");
        return BlankToNull(name);
    }

    private static string? ResolveStaffName(long? staffId, DisplayLookups lookups)
    {
        if (staffId is not long id || !lookups.Staff.TryGetValue(id, out var staff))
            return null;

        return BlankToNull(staff.FullName);
    }

    private static string? ResolvePlace(OrderBundle bundle, DisplayLookups lookups)
    {
        var pointId = bundle.Queue?.CurrentServicePointId ?? bundle.Queue?.ServicePointId;
        if (pointId is not long id || !lookups.Points.TryGetValue(id, out var point))
            return null;

        var pointName = BlankToNull(point.Name);
        if (pointName is not null)
            return pointName;

        if (point.RoomId is long roomId &&
            lookups.Rooms.TryGetValue(roomId, out var room))
        {
            return BlankToNull(room.Name);
        }

        return null;
    }

    private static string? ExtractFileName(string? reportFileUrl)
    {
        if (string.IsNullOrWhiteSpace(reportFileUrl))
            return null;

        var trimmed = reportFileUrl.Trim();
        if (!trimmed.StartsWith('/') &&
            !trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = trimmed;
        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0)
            path = path[..cut];

        var slash = path.LastIndexOf('/');
        if (slash < 0 || slash == path.Length - 1)
            return null;

        var name = path[(slash + 1)..];
        return BlankToNull(name);
    }

    private static string? BlankToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureActor(long actorUserId)
    {
        if (actorUserId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
    }

    private static ListRequest NormalizeListRequest(GetPatientDiagnosticResultsRequest? request)
    {
        var page = request?.Page ?? 1;
        var pageSize = request?.PageSize ?? 20;
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
            throw new ValidationException("Tham số phân trang không hợp lệ.");

        if (request?.PatientId is <= 0)
            throw new ValidationException("Mã hồ sơ bệnh nhân không hợp lệ.");

        if (request?.VisitId is <= 0)
            throw new ValidationException("Mã lượt khám không hợp lệ.");

        var visitCode = request?.VisitCode?.Trim();
        if (string.IsNullOrEmpty(visitCode))
            visitCode = null;
        else if (visitCode.Length > MaxIdentifierLength)
            throw new ValidationException("Mã lượt khám không hợp lệ.");
        else
            visitCode = visitCode.ToUpperInvariant();

        return new ListRequest(page, pageSize, request?.PatientId, request?.VisitId, visitCode);
    }

    private static OrderIdentifier ParseOrderIdentifier(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
            throw new ValidationException("Mã chỉ định cận lâm sàng không hợp lệ.");

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
            trimmed = trimmed[1..].TrimStart();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxIdentifierLength)
            throw new ValidationException("Mã chỉ định cận lâm sàng không hợp lệ.");

        if (trimmed.All(char.IsAsciiDigit))
        {
            if (!long.TryParse(trimmed, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) ||
                id <= 0)
            {
                throw new ValidationException("Mã chỉ định cận lâm sàng không hợp lệ.");
            }

            return new OrderIdentifier(trimmed, id, null);
        }

        return new OrderIdentifier(trimmed, null, trimmed.ToUpperInvariant());
    }

    private sealed record ListRequest(int Page, int PageSize, long? PatientId, long? VisitId, string? VisitCode);

    private sealed record OrderIdentifier(string Trimmed, long? Id, string? Code);

    private sealed record OrderBundle(
        DiagnosticOrder Order,
        Visit Visit,
        DiagnosticResult Result,
        IReadOnlyList<DiagnosticResultVersion> Versions,
        QueueEntry? Queue);

    private sealed record VisibleResult(
        OrderBundle Bundle,
        AppointmentReadModel.ScopeMember Member,
        bool IsPublished,
        DiagnosticResultVersion? Content,
        DateTime? SignedAt);

    private sealed record DisplayLookups(
        IReadOnlyDictionary<long, Service> Services,
        IReadOnlyDictionary<long, Hospital> Hospitals,
        IReadOnlyDictionary<long, Doctor> Doctors,
        IReadOnlyDictionary<long, Staff> Staff,
        IReadOnlyDictionary<long, QueueServicePoint> Points,
        IReadOnlyDictionary<long, HospitalRoom> Rooms);
}
