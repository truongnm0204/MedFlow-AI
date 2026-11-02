using System.Globalization;
using MedFlow.Application.DTOs.ExaminationSlots;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
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

    public async Task<ExaminationSlotAdminItemDto> CreateExaminationSlotAsync(
        CreateExaminationSlotRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Resolve & validate time window
        DateTime start;
        DateTime end;

        if (request.StartsAt.HasValue && request.EndsAt.HasValue)
        {
            start = request.StartsAt.Value;
            end = request.EndsAt.Value;
        }
        else if (!string.IsNullOrWhiteSpace(request.Date) &&
                 !string.IsNullOrWhiteSpace(request.StartTime) &&
                 !string.IsNullOrWhiteSpace(request.EndTime))
        {
            if (!DateTime.TryParseExact(request.Date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
                && !DateTime.TryParse(request.Date.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
            {
                throw new ArgumentException("Định dạng ngày không hợp lệ (YYYY-MM-DD).", nameof(request.Date));
            }

            if (!TimeOnly.TryParseExact(request.StartTime.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime)
                && !TimeOnly.TryParse(request.StartTime.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out startTime))
            {
                throw new ArgumentException("Định dạng giờ bắt đầu không hợp lệ (HH:mm).", nameof(request.StartTime));
            }

            if (!TimeOnly.TryParseExact(request.EndTime.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime)
                && !TimeOnly.TryParse(request.EndTime.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out endTime))
            {
                throw new ArgumentException("Định dạng giờ kết thúc không hợp lệ (HH:mm).", nameof(request.EndTime));
            }

            start = parsedDate.Date.Add(startTime.ToTimeSpan());
            end = parsedDate.Date.Add(endTime.ToTimeSpan());
        }
        else
        {
            throw new ArgumentException("Thời gian bắt đầu và kết thúc của khung giờ khám là bắt buộc.");
        }

        if (end <= start)
        {
            throw new ArgumentException("Giờ kết thúc phải sau giờ bắt đầu.");
        }

        // 2. Validate capacity & status
        var capacity = request.Capacity ?? 8;
        if (capacity < 1 || capacity > 100)
        {
            throw new ArgumentException("Sức chứa phải từ 1 đến 100 lượt.", nameof(request.Capacity));
        }

        var status = string.IsNullOrWhiteSpace(request.Status) ? "OPEN" : request.Status.Trim().ToUpperInvariant();
        if (status != "OPEN" && status != "CLOSED" && status != "FULL")
        {
            throw new ArgumentException("Trạng thái khung giờ không hợp lệ.", nameof(request.Status));
        }

        // 3. Resolve & validate entities
        Service? service = null;
        if (request.ServiceId.HasValue)
        {
            if (request.ServiceId.Value <= 0)
            {
                throw new ArgumentException("Mã dịch vụ không hợp lệ.", nameof(request.ServiceId));
            }

            service = await _db.Services
                .Include(s => s.Specialty)
                .FirstOrDefaultAsync(s => s.Id == request.ServiceId.Value, ct);

            if (service == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy dịch vụ khám với ID {request.ServiceId.Value}.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.ServiceCode))
        {
            var sCode = request.ServiceCode.Trim().ToLowerInvariant();
            service = await _db.Services
                .Include(s => s.Specialty)
                .FirstOrDefaultAsync(s => s.ServiceCode.ToLower() == sCode, ct);

            if (service == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy dịch vụ khám với mã {request.ServiceCode}.");
            }
        }

        if (service != null && service.IsActive == false)
        {
            throw new ArgumentException("Dịch vụ khám đang bị tạm ngưng hoặc không hoạt động.");
        }

        Doctor? doctor = null;
        if (request.DoctorId.HasValue)
        {
            if (request.DoctorId.Value <= 0)
            {
                throw new ArgumentException("Mã bác sĩ không hợp lệ.", nameof(request.DoctorId));
            }

            doctor = await _db.Doctors
                .Include(d => d.Staff)
                .Include(d => d.Specialty)
                .FirstOrDefaultAsync(d => d.Id == request.DoctorId.Value, ct);

            if (doctor == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy bác sĩ với ID {request.DoctorId.Value}.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.DoctorCode))
        {
            var dCode = request.DoctorCode.Trim().ToLowerInvariant();
            doctor = await _db.Doctors
                .Include(d => d.Staff)
                .Include(d => d.Specialty)
                .FirstOrDefaultAsync(d => d.DoctorCode.ToLower() == dCode, ct);

            if (doctor == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy bác sĩ với mã {request.DoctorCode}.");
            }
        }

        if (doctor != null && !doctor.IsActive)
        {
            throw new ArgumentException("Bác sĩ đang không ở trạng thái hoạt động.");
        }

        HospitalRoom? room = null;
        if (request.RoomId.HasValue)
        {
            if (request.RoomId.Value <= 0)
            {
                throw new ArgumentException("Mã buồng khám không hợp lệ.", nameof(request.RoomId));
            }

            room = await _db.HospitalRooms
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == request.RoomId.Value, ct);

            if (room == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy buồng khám với ID {request.RoomId.Value}.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.RoomCode))
        {
            var rCode = request.RoomCode.Trim().ToLowerInvariant();
            room = await _db.HospitalRooms
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.RoomCode.ToLower() == rCode, ct);

            if (room == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy buồng khám với mã {request.RoomCode}.");
            }
        }

        if (room != null && string.Equals(room.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Buồng khám đang ngừng hoạt động.");
        }

        // Must assign at least a doctor or a service
        if (service == null && doctor == null)
        {
            throw new ArgumentException("Vui lòng chỉ định dịch vụ khám hoặc bác sĩ cho khung giờ khám.");
        }

        // 4. Check for schedule conflicts (Doctor or Room overlapping)
        if (doctor != null)
        {
            var doctorConflict = await _db.ExaminationSlots.AsNoTracking()
                .AnyAsync(s => s.DoctorId == doctor.Id
                            && s.Status.ToUpper() != "CLOSED"
                            && s.StartsAt < end
                            && s.EndsAt > start, ct);

            if (doctorConflict)
            {
                var docName = doctor.Staff?.FullName ?? doctor.DoctorCode;
                throw new InvalidOperationException($"Xung đột lịch bác sĩ: Bác sĩ {docName} đã có ca khám trùng khoảng thời gian từ {start:HH:mm} đến {end:HH:mm}.");
            }
        }

        if (room != null)
        {
            var roomConflict = await _db.ExaminationSlots.AsNoTracking()
                .AnyAsync(s => s.RoomId == room.Id
                            && s.Status.ToUpper() != "CLOSED"
                            && s.StartsAt < end
                            && s.EndsAt > start, ct);

            if (roomConflict)
            {
                var roomName = room.RoomCode ?? room.Name;
                throw new InvalidOperationException($"Xung đột buồng khám: Phòng {roomName} đã được xếp lịch cho ca khám khác trong khoảng thời gian từ {start:HH:mm} đến {end:HH:mm}.");
            }
        }

        // 5. Persist slot
        var slot = new ExaminationSlot
        {
            ServiceId = service?.Id,
            DoctorId = doctor?.Id,
            RoomId = room?.Id,
            StartsAt = start,
            EndsAt = end,
            Capacity = capacity,
            Status = status
        };

        _db.ExaminationSlots.Add(slot);
        await _db.SaveChangesAsync(ct);

        // 6. Map and return created slot DTO
        var doctorName = doctor?.Staff?.FullName ?? doctor?.DoctorCode ?? string.Empty;
        var serviceName = service?.Name ?? string.Empty;
        var serviceCode = service?.ServiceCode ?? string.Empty;
        var serviceType = service?.ServiceType ?? "OUTPATIENT";
        if (string.Equals(serviceType, "EXAMINATION", StringComparison.OrdinalIgnoreCase))
        {
            serviceType = "OUTPATIENT";
        }

        var specialty = doctor?.Specialty?.Name ?? service?.Specialty?.Name ?? string.Empty;
        var roomCode = room?.RoomCode ?? room?.Name ?? string.Empty;
        var facility = room?.Department?.Name ?? "MedFlow Cơ sở 1";

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
            Room: roomCode,
            CapacityBooked: 0,
            CapacityMax: capacity,
            Date: start.ToString("yyyy-MM-dd"),
            DateDisplay: start.ToString("dd/MM"),
            StartTime: start.ToString("HH:mm"),
            EndTime: end.ToString("HH:mm"),
            Status: status,
            Facility: facility,
            NeedsReview: false
        );
    }
}

