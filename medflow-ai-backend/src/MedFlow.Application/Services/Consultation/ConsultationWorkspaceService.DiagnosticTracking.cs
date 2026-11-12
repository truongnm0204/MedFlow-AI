using System.Globalization;
using MedFlow.Application.DTOs.Consultation;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Doctors;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Consultation;

public sealed partial class ConsultationWorkspaceService
{
    public async Task<DiagnosticResultsTrackingResponseDto> GetDiagnosticResultsTrackingAsync(
        long actorUserId,
        GetDiagnosticResultsTrackingRequest? request = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var todayVn = DateOnly.FromDateTime(AppointmentBookingRules.GetVietnamNow(_timeProvider));
        var filter = ValidateDiagnosticResultsTrackingRequest(
            request ?? new GetDiagnosticResultsTrackingRequest(),
            todayVn);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var doctorAccess = await EnsureDoctorAccessAsync(actorUserId, nowUtc, requiredPermission: null, ct);

        var start = filter.Date.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);

        List<long> candidateVisitIds;
        try
        {
            candidateVisitIds = await _db.DiagnosticOrders
                .AsNoTracking()
                .Where(o => o.VisitId.HasValue && o.OrderedAt >= start && o.OrderedAt < end)
                .Select(o => o.VisitId!.Value)
                .Distinct()
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            candidateVisitIds = [];
        }

        var doctorId = doctorAccess.Doctor.Id;
        List<Visit> candidateVisits = [];
        if (candidateVisitIds.Count > 0)
        {
            try
            {
                candidateVisits = await _db.Visits
                    .AsNoTracking()
                    .Where(v =>
                        candidateVisitIds.Contains(v.Id) &&
                        (v.AssignedDoctorId == doctorId ||
                         (v.AssignedDoctorId == null && v.CompletedByDoctorId == doctorId)))
                    .ToListAsync(ct);
            }
            catch (NotSupportedException)
            {
                candidateVisits = [];
            }
        }

        var keptVisits = candidateVisits
            .Where(v =>
                AppointmentBookingRules.NormalizeStatusKey(v.Status) is not ("CANCELLED" or "NO_SHOW"))
            .ToList();

        var keptVisitIds = keptVisits.Select(v => v.Id).Distinct().ToList();
        List<DiagnosticOrder> allVisitOrders = [];
        if (keptVisitIds.Count > 0)
        {
            try
            {
                allVisitOrders = await _db.DiagnosticOrders
                    .AsNoTracking()
                    .Where(o => o.VisitId.HasValue && keptVisitIds.Contains(o.VisitId.Value))
                    .ToListAsync(ct);
            }
            catch (NotSupportedException)
            {
                allVisitOrders = [];
            }
        }

        var activeOrders = allVisitOrders
            .Where(o =>
                o.VisitId.HasValue &&
                AppointmentBookingRules.NormalizeStatusKey(o.Status) != "CANCELLED")
            .ToList();

        var ordersByVisitId = activeOrders
            .GroupBy(o => o.VisitId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<DiagnosticOrder>)g
                    .OrderBy(o => o.OrderedAt)
                    .ThenBy(o => o.Id)
                    .ToList());

        var trackableVisits = keptVisits
            .Where(v => ordersByVisitId.TryGetValue(v.Id, out var visitOrders) && visitOrders.Count > 0)
            .ToList();

        var bundle = await LoadDiagnosticResultsBundleAsync(activeOrders, ct);

        var queueEntryIds = activeOrders
            .Where(o => o.QueueEntryId.HasValue)
            .Select(o => o.QueueEntryId!.Value)
            .Distinct()
            .ToList();
        Dictionary<long, QueueEntry> queueEntriesById = [];
        if (queueEntryIds.Count > 0)
        {
            try
            {
                queueEntriesById = await _db.QueueEntries
                    .AsNoTracking()
                    .Where(q => queueEntryIds.Contains(q.Id))
                    .ToDictionaryAsync(q => q.Id, ct);
            }
            catch (NotSupportedException)
            {
                queueEntriesById = [];
            }
        }

        var serviceIds = activeOrders
            .Where(o => o.ServiceId.HasValue)
            .Select(o => o.ServiceId!.Value)
            .Distinct()
            .ToList();
        Dictionary<long, Service> servicesById = [];
        if (serviceIds.Count > 0)
        {
            try
            {
                servicesById = await _db.Services
                    .AsNoTracking()
                    .Where(s => serviceIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, ct);
            }
            catch (NotSupportedException)
            {
                servicesById = [];
            }
        }

        var orderingDoctorIds = activeOrders
            .Where(o => o.OrderingDoctorId.HasValue)
            .Select(o => o.OrderingDoctorId!.Value)
            .Distinct()
            .ToList();
        Dictionary<long, Doctor> doctorsById = [];
        if (orderingDoctorIds.Count > 0)
        {
            try
            {
                doctorsById = await _db.Doctors
                    .AsNoTracking()
                    .Include(d => d.Staff)
                    .Where(d => orderingDoctorIds.Contains(d.Id))
                    .ToDictionaryAsync(d => d.Id, ct);
            }
            catch (NotSupportedException)
            {
                doctorsById = [];
            }
        }

        var patientIds = trackableVisits
            .Where(v => v.PatientId.HasValue)
            .Select(v => v.PatientId!.Value)
            .Distinct()
            .ToList();
        Dictionary<long, Patient> patientsById = [];
        if (patientIds.Count > 0)
        {
            try
            {
                patientsById = await _db.Patients
                    .AsNoTracking()
                    .Where(p => patientIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, ct);
            }
            catch (NotSupportedException)
            {
                patientsById = [];
            }
        }

        var visitDtos = new List<DiagnosticResultsTrackingVisitDto>(trackableVisits.Count);
        foreach (var visit in trackableVisits)
        {
            var visitOrders = ordersByVisitId[visit.Id];
            var orderDtos = new List<DiagnosticResultsTrackingOrderDto>(visitOrders.Count);

            var signedOrders = 0;
            var awaitingSignOrders = 0;
            var inProgressOrders = 0;
            var pendingOrders = 0;
            var mandatoryOrders = 0;
            var mandatorySignedOrders = 0;
            DateTime? latestOrderedAt = null;

            foreach (var order in visitOrders)
            {
                bundle.ResultsByOrderId.TryGetValue(order.Id, out var result);
                var latestVersion = result is not null &&
                                    bundle.VersionsByResultId.TryGetValue(result.Id, out var versions) &&
                                    versions.Count > 0
                    ? versions[0]
                    : null;

                var queueEntry = order.QueueEntryId.HasValue &&
                                 queueEntriesById.TryGetValue(order.QueueEntryId.Value, out var qe)
                    ? qe
                    : null;

                var state = ResolveDiagnosticOrderResultState(order, queueEntry, result, latestVersion);
                var orderStatus = string.IsNullOrWhiteSpace(order.Status)
                    ? "ORDERED"
                    : order.Status.Trim().ToUpperInvariant();
                var queueStatus = queueEntry is not null ? NormalizeQueueStatus(queueEntry.Status) : null;

                string orderTrackingStatus;
                if (state.IsResultReady)
                {
                    orderTrackingStatus = "SIGNED";
                    signedOrders++;
                }
                else if (result is not null)
                {
                    orderTrackingStatus = "AWAITING_SIGN";
                    awaitingSignOrders++;
                }
                else if (orderStatus == "IN_PROGRESS" || queueStatus is "CALLED" or "IN_PROGRESS")
                {
                    orderTrackingStatus = "IN_PROGRESS";
                    inProgressOrders++;
                }
                else
                {
                    orderTrackingStatus = "PENDING";
                    pendingOrders++;
                }

                var requiredForConclusion = order.RequiredForConclusion == true;
                if (requiredForConclusion)
                {
                    mandatoryOrders++;
                    if (orderTrackingStatus == "SIGNED")
                    {
                        mandatorySignedOrders++;
                    }
                }

                if (order.OrderedAt.HasValue &&
                    (!latestOrderedAt.HasValue || order.OrderedAt.Value > latestOrderedAt.Value))
                {
                    latestOrderedAt = order.OrderedAt.Value;
                }

                var service = order.ServiceId.HasValue &&
                              servicesById.TryGetValue(order.ServiceId.Value, out var svc)
                    ? svc
                    : null;

                var orderingDoctor = order.OrderingDoctorId.HasValue &&
                                     doctorsById.TryGetValue(order.OrderingDoctorId.Value, out var doc)
                    ? doc
                    : doctorAccess.Doctor;
                var docTitle = string.IsNullOrWhiteSpace(orderingDoctor.ProfessionalTitle)
                    ? "BS"
                    : orderingDoctor.ProfessionalTitle.Trim();
                var docFullName = orderingDoctor.Staff?.FullName ?? orderingDoctor.DoctorCode;
                var orderingDoctorName = DoctorCatalogService.FormatDisplayName(docFullName, docTitle);

                string? assigneeStaffName = null;
                if (latestVersion?.EnteredByStaffId.HasValue == true &&
                    bundle.StaffById.TryGetValue(latestVersion.EnteredByStaffId.Value, out var enteredStaff))
                {
                    assigneeStaffName = enteredStaff.FullName;
                }
                else if (order.ConfirmedByStaffId.HasValue &&
                         bundle.StaffById.TryGetValue(order.ConfirmedByStaffId.Value, out var confirmedStaff))
                {
                    assigneeStaffName = confirmedStaff.FullName;
                }

                var resultSummary = !string.IsNullOrWhiteSpace(latestVersion?.Conclusion)
                    ? latestVersion.Conclusion
                    : !string.IsNullOrWhiteSpace(latestVersion?.ResultText)
                        ? latestVersion.ResultText
                        : null;

                var signedAt = state.IsResultReady ? state.ResultFinalizedAt : null;
                string? signedByStaffName = null;
                if (state.IsResultReady)
                {
                    if (result?.FinalizedByStaffId.HasValue == true &&
                        bundle.StaffById.TryGetValue(result.FinalizedByStaffId.Value, out var finalizedStaff))
                    {
                        signedByStaffName = finalizedStaff.FullName;
                    }
                    else if (latestVersion?.VerifiedByStaffId.HasValue == true &&
                             bundle.StaffById.TryGetValue(latestVersion.VerifiedByStaffId.Value, out var verifiedStaff))
                    {
                        signedByStaffName = verifiedStaff.FullName;
                    }
                }

                orderDtos.Add(new DiagnosticResultsTrackingOrderDto(
                    OrderId: order.Id,
                    OrderCode: FormatOrderCode(order, filter.Date),
                    ServiceId: order.ServiceId,
                    ServiceCode: service?.ServiceCode,
                    ServiceName: service?.Name,
                    Priority: string.IsNullOrWhiteSpace(order.Priority)
                        ? null
                        : order.Priority.Trim().ToUpperInvariant(),
                    RequiredForConclusion: requiredForConclusion,
                    OrderStatus: orderStatus,
                    TrackingStatus: orderTrackingStatus,
                    ResultStatus: state.ResultStatus,
                    OrderingDoctorId: order.OrderingDoctorId ?? orderingDoctor.Id,
                    OrderingDoctorName: orderingDoctorName,
                    OrderedAt: order.OrderedAt,
                    AssigneeStaffName: assigneeStaffName,
                    ResultSummary: resultSummary,
                    SignedAt: signedAt,
                    SignedByStaffName: signedByStaffName));
            }

            var totalOrders = orderDtos.Count;
            var completionPercent = signedOrders * 100 / totalOrders;
            var visitTrackingStatus = signedOrders == totalOrders
                ? "COMPLETED"
                : pendingOrders == 0 && inProgressOrders == 0
                    ? "PENDING_SIGN"
                    : "IN_PROGRESS";

            var visitCode = !string.IsNullOrWhiteSpace(visit.VisitCode)
                ? visit.VisitCode.Trim()
                : $"VIS-{filter.Date:yyyyMM}-{visit.Id:D4}";
            var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);

            var patient = visit.PatientId.HasValue &&
                          patientsById.TryGetValue(visit.PatientId.Value, out var foundPatient)
                ? foundPatient
                : null;

            visitDtos.Add(new DiagnosticResultsTrackingVisitDto(
                VisitId: visit.Id,
                VisitCode: visitCode,
                VisitStatus: visitStatus,
                PatientId: visit.PatientId,
                PatientCode: patient?.PatientCode,
                PatientName: patient?.FullName,
                TrackingStatus: visitTrackingStatus,
                TotalOrders: totalOrders,
                SignedOrders: signedOrders,
                AwaitingSignOrders: awaitingSignOrders,
                InProgressOrders: inProgressOrders,
                PendingOrders: pendingOrders,
                MandatoryOrders: mandatoryOrders,
                MandatorySignedOrders: mandatorySignedOrders,
                CompletionPercent: completionPercent,
                LatestOrderedAt: latestOrderedAt,
                Orders: orderDtos));
        }

        var statsTotalOrders = visitDtos.Sum(v => v.TotalOrders);
        var statsSignedOrders = visitDtos.Sum(v => v.SignedOrders);
        var stats = new DiagnosticResultsTrackingStatsDto(
            TotalVisits: visitDtos.Count,
            CompletedVisits: visitDtos.Count(v => v.TrackingStatus == "COMPLETED"),
            InProgressVisits: visitDtos.Count(v => v.TrackingStatus == "IN_PROGRESS"),
            PendingSignVisits: visitDtos.Count(v => v.TrackingStatus == "PENDING_SIGN"),
            TotalOrders: statsTotalOrders,
            SignedOrders: statsSignedOrders,
            WaitingOrders: statsTotalOrders - statsSignedOrders);

        IEnumerable<DiagnosticResultsTrackingVisitDto> filtered = visitDtos;
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            filtered = filtered.Where(v => v.TrackingStatus == filter.Status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword;
            filtered = filtered.Where(v =>
                v.VisitCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                (v.PatientCode is not null && v.PatientCode.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                (v.PatientName is not null && v.PatientName.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                v.Orders.Any(o =>
                    o.OrderCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                    (o.ServiceName is not null && o.ServiceName.Contains(kw, StringComparison.OrdinalIgnoreCase))));
        }

        var filteredList = filtered
            .OrderByDescending(v => v.LatestOrderedAt ?? DateTime.MinValue)
            .ThenByDescending(v => v.VisitId)
            .ToList();

        var totalCount = filteredList.Count;
        var totalPages = (int)Math.Ceiling(totalCount / (double)filter.PageSize);
        var pagedItems = filteredList
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToList();

        var actorDocTitle = string.IsNullOrWhiteSpace(doctorAccess.Doctor.ProfessionalTitle)
            ? "BS"
            : doctorAccess.Doctor.ProfessionalTitle.Trim();
        var doctorName = DoctorCatalogService.FormatDisplayName(
            doctorAccess.Staff.FullName,
            actorDocTitle);

        return new DiagnosticResultsTrackingResponseDto(
            Date: filter.Date,
            HospitalId: doctorAccess.Hospital.Id,
            DoctorId: doctorId,
            DoctorCode: doctorAccess.Doctor.DoctorCode,
            DoctorName: doctorName,
            GeneratedAtUtc: nowUtc,
            Stats: stats,
            TotalCount: totalCount,
            Page: filter.Page,
            PageSize: filter.PageSize,
            TotalPages: totalPages,
            Items: pagedItems);
    }

    private static ValidatedDiagnosticResultsTrackingFilter ValidateDiagnosticResultsTrackingRequest(
        GetDiagnosticResultsTrackingRequest request,
        DateOnly todayVn)
    {
        var date = todayVn;
        if (!string.IsNullOrWhiteSpace(request.Date))
        {
            var trimmedDate = request.Date.Trim();
            if (!DateOnly.TryParseExact(
                    trimmedDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out date))
            {
                throw new ValidationException(
                    "Ngày theo dõi kết quả cận lâm sàng không hợp lệ (định dạng YYYY-MM-DD).");
            }
        }

        string? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var normalizedStatus = request.Status.Trim().ToUpperInvariant();
            if (normalizedStatus != "ALL")
            {
                if (normalizedStatus is not ("COMPLETED" or "IN_PROGRESS" or "PENDING_SIGN"))
                {
                    throw new ValidationException(
                        "Trạng thái theo dõi kết quả cận lâm sàng không hợp lệ.");
                }

                status = normalizedStatus;
            }
        }

        string? keyword = null;
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var trimmedKeyword = request.Keyword.Trim();
            if (trimmedKeyword.Length > 100 ||
                trimmedKeyword.Contains('<') ||
                trimmedKeyword.Contains('>'))
            {
                throw new ValidationException(
                    "Từ khóa tìm kiếm không hợp lệ (tối đa 100 ký tự, không chứa ký tự < >).");
            }

            keyword = trimmedKeyword;
        }

        if (request.Page < 1)
        {
            throw new ValidationException("Số trang phải lớn hơn hoặc bằng 1.");
        }

        if (request.PageSize is < 1 or > 100)
        {
            throw new ValidationException("Số lượt khám mỗi trang phải từ 1 đến 100.");
        }

        return new ValidatedDiagnosticResultsTrackingFilter(
            Date: date,
            Status: status,
            Keyword: keyword,
            Page: request.Page,
            PageSize: request.PageSize);
    }

    private sealed record ValidatedDiagnosticResultsTrackingFilter(
        DateOnly Date,
        string? Status,
        string? Keyword,
        int Page,
        int PageSize);
}
