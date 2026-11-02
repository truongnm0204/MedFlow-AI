using System.Globalization;
using MedFlow.Application.DTOs.ExaminationSlots;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.ExaminationSlots;

public sealed class ExaminationSlotAdminService : IExaminationSlotAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public ExaminationSlotAdminService(IApplicationDbContext db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ExaminationSlotAdminListResponseDto> GetExaminationSlotListAsync(
        GetExaminationSlotListRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Page < 1)
        {
            throw new ArgumentException("Trang yêu cầu phải lớn hơn hoặc bằng 1.", nameof(request.Page));
        }

        if (request.PageSize < 1 || request.PageSize > 100)
        {
            throw new ArgumentException("Số lượng bản ghi trên một trang phải từ 1 đến 100.", nameof(request.PageSize));
        }

        var baseQuery = _db.ExaminationSlots.AsNoTracking();

        // 1. Calculate overall metrics across all examination slots
        var nowLocal = _timeProvider.GetLocalNow().DateTime;
        var today = nowLocal.Date;
        var in7Days = today.AddDays(8); // next 7 full days

        var allSlotsForMetrics = await baseQuery
            .Select(s => new
            {
                s.Id,
                s.Status,
                s.StartsAt,
                s.Capacity,
                s.DoctorId,
                s.ServiceId,
                s.RoomId
            })
            .ToListAsync(ct);

        var allActiveAppointments = await _db.Appointments.AsNoTracking()
            .Where(a => a.SlotId != null && (a.Status == null || a.Status.ToUpper() != "CANCELLED"))
            .Select(a => a.SlotId!.Value)
            .ToListAsync(ct);

        var appointmentCountMap = allActiveAppointments
            .GroupBy(slotId => slotId)
            .ToDictionary(g => g.Key, g => g.Count());

        var totalAll = allSlotsForMetrics.Count;
        var openCount = allSlotsForMetrics.Count(s => string.Equals(s.Status, "OPEN", StringComparison.OrdinalIgnoreCase));
        var bookedCount = allSlotsForMetrics.Count(s => appointmentCountMap.ContainsKey(s.Id) && appointmentCountMap[s.Id] > 0);
        var upcomingCount = allSlotsForMetrics.Count(s => s.StartsAt.HasValue && s.StartsAt.Value >= today && s.StartsAt.Value < in7Days);
        var reviewCount = allSlotsForMetrics.Count(s =>
            string.Equals(s.Status, "CLOSED", StringComparison.OrdinalIgnoreCase) ||
            s.DoctorId == null ||
            s.ServiceId == null ||
            s.RoomId == null ||
            (s.Capacity.HasValue && s.Capacity.Value > 0 && appointmentCountMap.TryGetValue(s.Id, out var count) && count > s.Capacity.Value));

        var metrics = new ExaminationSlotMetricsDto(
            Total: totalAll,
            OpenCount: openCount,
            BookedCount: bookedCount,
            UpcomingCount: upcomingCount,
            ReviewCount: reviewCount
        );

        // 2. Build filtered query with navigations
        var filteredQuery = baseQuery
            .Include(s => s.Doctor)
                .ThenInclude(d => d!.Staff)
            .Include(s => s.Doctor)
                .ThenInclude(d => d!.Specialty)
            .Include(s => s.Service)
                .ThenInclude(srv => srv!.Specialty)
            .Include(s => s.Room)
                .ThenInclude(r => r!.Department)
            .AsQueryable();

        // Keyword filter
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim().ToLowerInvariant();

            long? keywordSlotId = null;
            if (kw.StartsWith("slt-", StringComparison.OrdinalIgnoreCase))
            {
                var rawNum = kw["slt-".Length..].TrimStart('0');
                if (long.TryParse(string.IsNullOrEmpty(rawNum) ? "0" : rawNum, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId))
                {
                    keywordSlotId = parsedId;
                }
            }
            else if (long.TryParse(kw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId))
            {
                keywordSlotId = parsedId;
            }

            filteredQuery = filteredQuery.Where(s =>
                (keywordSlotId.HasValue && s.Id == keywordSlotId.Value) ||
                (s.Doctor != null && s.Doctor.Staff != null && s.Doctor.Staff.FullName.ToLower().Contains(kw)) ||
                (s.Doctor != null && s.Doctor.DoctorCode.ToLower().Contains(kw)) ||
                (s.Service != null && s.Service.Name.ToLower().Contains(kw)) ||
                (s.Service != null && s.Service.ServiceCode.ToLower().Contains(kw)) ||
                (s.Doctor != null && s.Doctor.Specialty != null && s.Doctor.Specialty.Name.ToLower().Contains(kw)) ||
                (s.Service != null && s.Service.Specialty != null && s.Service.Specialty.Name.ToLower().Contains(kw)) ||
                (s.Room != null && s.Room.RoomCode.ToLower().Contains(kw)) ||
                (s.Room != null && s.Room.Name.ToLower().Contains(kw)));
        }

        // Exact Date filter (yyyy-MM-dd)
        if (!string.IsNullOrWhiteSpace(request.Date))
        {
            if (DateTime.TryParseExact(request.Date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
                || DateTime.TryParse(request.Date.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
            {
                var dayStart = parsedDate.Date;
                var dayEnd = dayStart.AddDays(1);
                filteredQuery = filteredQuery.Where(s => s.StartsAt.HasValue && s.StartsAt.Value >= dayStart && s.StartsAt.Value < dayEnd);
            }
        }

        // Date range filters
        if (!string.IsNullOrWhiteSpace(request.FromDate))
        {
            if (DateTime.TryParse(request.FromDate.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDate))
            {
                var fromStart = fromDate.Date;
                filteredQuery = filteredQuery.Where(s => s.StartsAt.HasValue && s.StartsAt.Value >= fromStart);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.ToDate))
        {
            if (DateTime.TryParse(request.ToDate.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDate))
            {
                var toEnd = toDate.Date.AddDays(1);
                filteredQuery = filteredQuery.Where(s => s.StartsAt.HasValue && s.StartsAt.Value < toEnd);
            }
        }

        // Doctor filter
        if (request.DoctorId.HasValue)
        {
            filteredQuery = filteredQuery.Where(s => s.DoctorId == request.DoctorId.Value);
        }

        // Service filter
        if (request.ServiceId.HasValue)
        {
            filteredQuery = filteredQuery.Where(s => s.ServiceId == request.ServiceId.Value);
        }

        // Room filter
        if (request.RoomId.HasValue)
        {
            filteredQuery = filteredQuery.Where(s => s.RoomId == request.RoomId.Value);
        }

        // Specialty ID filter
        if (request.SpecialtyId.HasValue)
        {
            filteredQuery = filteredQuery.Where(s =>
                (s.Doctor != null && s.Doctor.SpecialtyId == request.SpecialtyId.Value) ||
                (s.Service != null && s.Service.SpecialtyId == request.SpecialtyId.Value));
        }

        // Specialty Name filter
        if (!string.IsNullOrWhiteSpace(request.Specialty))
        {
            var spec = request.Specialty.Trim().ToLowerInvariant();
            filteredQuery = filteredQuery.Where(s =>
                (s.Doctor != null && s.Doctor.Specialty != null && s.Doctor.Specialty.Name.ToLower().Contains(spec)) ||
                (s.Service != null && s.Service.Specialty != null && s.Service.Specialty.Name.ToLower().Contains(spec)));
        }

        // Status filter
        if (!string.IsNullOrWhiteSpace(request.Status) && !string.Equals(request.Status, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var st = request.Status.Trim().ToUpperInvariant();
            if (st == "CLOSED")
            {
                filteredQuery = filteredQuery.Where(s => s.Status.ToUpper() == "CLOSED");
            }
            else if (st == "OPEN")
            {
                filteredQuery = filteredQuery.Where(s => s.Status.ToUpper() == "OPEN");
            }
            else if (st == "FULL")
            {
                filteredQuery = filteredQuery.Where(s => s.Status.ToUpper() == "FULL");
            }
        }

        // 3. Count total matching rows and calculate total pages
        var totalFiltered = await filteredQuery.CountAsync(ct);
        var totalPages = totalFiltered == 0 ? 1 : (int)Math.Ceiling((double)totalFiltered / request.PageSize);

        // 4. Fetch page slice
        var pageEntities = await filteredQuery
            .OrderBy(s => s.StartsAt)
            .ThenBy(s => s.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        // 5. Query active appointment counts for the page slice
        var pageSlotIds = pageEntities.Select(s => s.Id).ToList();
        var pageAppointments = await _db.Appointments.AsNoTracking()
            .Where(a => a.SlotId != null && pageSlotIds.Contains(a.SlotId.Value) && (a.Status == null || a.Status.ToUpper() != "CANCELLED"))
            .GroupBy(a => a.SlotId!.Value)
            .Select(g => new { SlotId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SlotId, x => x.Count, ct);

        // 6. Map to DTOs
        var items = pageEntities.Select(slot =>
        {
            var booked = pageAppointments.TryGetValue(slot.Id, out var count) ? count : 0;
            var maxCap = slot.Capacity ?? 0;
            var startsAt = slot.StartsAt;
            var endsAt = slot.EndsAt;

            var doctorName = slot.Doctor?.Staff?.FullName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(doctorName) && !string.IsNullOrWhiteSpace(slot.Doctor?.DoctorCode))
            {
                doctorName = slot.Doctor.DoctorCode;
            }

            var serviceName = slot.Service?.Name ?? string.Empty;
            var serviceCode = slot.Service?.ServiceCode ?? string.Empty;
            var serviceType = slot.Service?.ServiceType ?? "OUTPATIENT";
            if (string.Equals(serviceType, "EXAMINATION", StringComparison.OrdinalIgnoreCase))
            {
                serviceType = "OUTPATIENT";
            }

            var specialty = slot.Doctor?.Specialty?.Name
                            ?? slot.Service?.Specialty?.Name
                            ?? string.Empty;

            var room = slot.Room?.RoomCode ?? slot.Room?.Name ?? string.Empty;
            var facility = slot.Room?.Department?.Name ?? "MedFlow Cơ sở 1";

            var rawStatus = (slot.Status ?? string.Empty).Trim().ToUpperInvariant();
            string displayStatus;
            if (rawStatus == "CLOSED")
            {
                displayStatus = "CLOSED";
            }
            else if (rawStatus == "FULL" || (maxCap > 0 && booked >= maxCap))
            {
                displayStatus = "FULL";
            }
            else if (rawStatus == "OPEN")
            {
                displayStatus = "OPEN";
            }
            else
            {
                displayStatus = string.IsNullOrWhiteSpace(rawStatus) ? "OPEN" : rawStatus;
            }

            var needsReview = (maxCap > 0 && booked > maxCap) ||
                              slot.DoctorId == null ||
                              slot.ServiceId == null ||
                              slot.RoomId == null ||
                              (slot.Doctor != null && !slot.Doctor.IsActive) ||
                              (slot.Service != null && slot.Service.IsActive == false) ||
                              (slot.Room != null && string.Equals(slot.Room.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase));

            return new ExaminationSlotAdminItemDto(
                Id: slot.Id,
                Code: $"SLT-{slot.Id:D3}",
                DoctorId: slot.DoctorId,
                DoctorName: doctorName,
                ServiceId: slot.ServiceId,
                ServiceName: serviceName,
                ServiceCode: serviceCode,
                ServiceType: serviceType,
                Specialty: specialty,
                RoomId: slot.RoomId,
                Room: room,
                CapacityBooked: booked,
                CapacityMax: maxCap,
                Date: startsAt?.ToString("yyyy-MM-dd") ?? string.Empty,
                DateDisplay: startsAt?.ToString("dd/MM") ?? string.Empty,
                StartTime: startsAt?.ToString("HH:mm") ?? string.Empty,
                EndTime: endsAt?.ToString("HH:mm") ?? string.Empty,
                Status: displayStatus,
                Facility: facility,
                NeedsReview: needsReview
            );
        }).ToList();

        return new ExaminationSlotAdminListResponseDto(
            Data: items,
            Total: totalFiltered,
            Page: request.Page,
            PageSize: request.PageSize,
            TotalPages: totalPages,
            Metrics: metrics
        );
    }
}
