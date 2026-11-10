using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.DTOs.ExaminationSlots;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.ExaminationSlots;

public sealed class ExaminationSlotAdminService : IExaminationSlotAdminService
{
    public const string SlotClosedAuditAction = "EXAMINATION_SLOT_CLOSED";
    public const string SlotReopenedAuditAction = "EXAMINATION_SLOT_REOPENED";
    public const string SlotAuditEntityType = "ExaminationSlot";

    public const string ActionKeepExisting = "KEEP_EXISTING";
    public const string ActionRescheduleRequired = "RESCHEDULE_REQUIRED";

    private const int MinOtherNoteLength = 5;
    private const int MaxNoteLength = 500;

    private static readonly IReadOnlyDictionary<string, string> CloseReasonLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOCTOR_UNAVAILABLE"] = "Bác sĩ bận đột xuất / công tác / hội chẩn chuyên môn",
            ["EQUIPMENT_MAINTENANCE"] = "Bảo trì buồng khám / trang thiết bị chuyên dụng",
            ["SCHEDULE_ADJUSTMENT"] = "Điều chỉnh phân bổ lịch hoạt động của khoa / phòng",
            ["EMERGENCY"] = "Sự cố vận hành khẩn cấp / bất khả kháng",
            ["OTHER"] = "Lý do khác (ghi rõ lý do)",
        };

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
        var closedSlotIds = pageEntities
            .Where(s => string.Equals(s.Status?.Trim(), "CLOSED", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Id)
            .ToList();

        var closureMetadataMap = closedSlotIds.Count > 0
            ? await TryLoadClosureMetadataMapAsync(closedSlotIds, ct)
            : new Dictionary<long, SlotClosureMetadata>();

        // 6. Map to DTOs
        var items = pageEntities.Select(slot =>
        {
            var booked = pageAppointments.TryGetValue(slot.Id, out var count) ? count : 0;
            closureMetadataMap.TryGetValue(slot.Id, out var closureMeta);
            return MapSlotToItemDto(slot, booked, closureMeta);
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

    public async Task<ExaminationSlotAdminItemDto> UpdateExaminationSlotAsync(
        long id,
        UpdateExaminationSlotRequest request,
        CancellationToken ct = default)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Mã khung giờ khám không hợp lệ.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(request);

        // Fail-fast input validation before database calls
        if (request.Capacity.HasValue && (request.Capacity.Value < 1 || request.Capacity.Value > 100))
        {
            throw new ArgumentException("Sức chứa phải từ 1 đến 100 lượt.", nameof(request.Capacity));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var st = request.Status.Trim().ToUpperInvariant();
            if (st != "OPEN" && st != "CLOSED" && st != "FULL")
            {
                throw new ArgumentException("Trạng thái khung giờ không hợp lệ.", nameof(request.Status));
            }
        }

        if (request.ServiceId.HasValue && request.ServiceId.Value <= 0)
        {
            throw new ArgumentException("Mã dịch vụ không hợp lệ.", nameof(request.ServiceId));
        }

        if (request.DoctorId.HasValue && request.DoctorId.Value <= 0)
        {
            throw new ArgumentException("Mã bác sĩ không hợp lệ.", nameof(request.DoctorId));
        }

        if (request.RoomId.HasValue && request.RoomId.Value <= 0)
        {
            throw new ArgumentException("Mã buồng khám không hợp lệ.", nameof(request.RoomId));
        }

        if (request.StartsAt.HasValue && request.EndsAt.HasValue && request.EndsAt.Value <= request.StartsAt.Value)
        {
            throw new ArgumentException("Giờ kết thúc phải sau giờ bắt đầu.");
        }

        if (!string.IsNullOrWhiteSpace(request.StartTime) && !string.IsNullOrWhiteSpace(request.EndTime))
        {
            var hasStart = TimeOnly.TryParseExact(request.StartTime.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sTime)
                           || TimeOnly.TryParse(request.StartTime.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out sTime);
            var hasEnd = TimeOnly.TryParseExact(request.EndTime.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var eTime)
                         || TimeOnly.TryParse(request.EndTime.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out eTime);
            if (hasStart && hasEnd && eTime <= sTime)
            {
                throw new ArgumentException("Giờ kết thúc phải sau giờ bắt đầu.", nameof(request.EndTime));
            }
        }

        var slot = await _db.ExaminationSlots
            .Include(s => s.Doctor).ThenInclude(d => d!.Staff)
            .Include(s => s.Doctor).ThenInclude(d => d!.Specialty)
            .Include(s => s.Service).ThenInclude(srv => srv!.Specialty)
            .Include(s => s.Room).ThenInclude(r => r!.Department)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (slot == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy khung giờ khám với ID {id}.");
        }

        // Check active bookings count (Do not invalidate existing bookings)
        var activeBookings = await _db.Appointments.AsNoTracking()
            .CountAsync(a => a.SlotId == id && (a.Status == null || a.Status.ToUpper() != "CANCELLED"), ct);

        // 1. Resolve & validate time window
        DateTime targetStart;
        DateTime targetEnd;

        if (request.StartsAt.HasValue && request.EndsAt.HasValue)
        {
            targetStart = request.StartsAt.Value;
            targetEnd = request.EndsAt.Value;
        }
        else if (!string.IsNullOrWhiteSpace(request.Date) ||
                 !string.IsNullOrWhiteSpace(request.StartTime) ||
                 !string.IsNullOrWhiteSpace(request.EndTime))
        {
            var baseDate = slot.StartsAt ?? DateTime.Today;

            if (!string.IsNullOrWhiteSpace(request.Date))
            {
                if (!DateTime.TryParseExact(request.Date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
                    && !DateTime.TryParse(request.Date.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                {
                    throw new ArgumentException("Định dạng ngày không hợp lệ (YYYY-MM-DD).", nameof(request.Date));
                }
                baseDate = parsedDate.Date;
            }

            var startTime = slot.StartsAt.HasValue ? TimeOnly.FromDateTime(slot.StartsAt.Value) : new TimeOnly(8, 0);
            if (!string.IsNullOrWhiteSpace(request.StartTime))
            {
                if (!TimeOnly.TryParseExact(request.StartTime.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedStart)
                    && !TimeOnly.TryParse(request.StartTime.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedStart))
                {
                    throw new ArgumentException("Định dạng giờ bắt đầu không hợp lệ (HH:mm).", nameof(request.StartTime));
                }
                startTime = parsedStart;
            }

            var endTime = slot.EndsAt.HasValue ? TimeOnly.FromDateTime(slot.EndsAt.Value) : new TimeOnly(8, 30);
            if (!string.IsNullOrWhiteSpace(request.EndTime))
            {
                if (!TimeOnly.TryParseExact(request.EndTime.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedEnd)
                    && !TimeOnly.TryParse(request.EndTime.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedEnd))
                {
                    throw new ArgumentException("Định dạng giờ kết thúc không hợp lệ (HH:mm).", nameof(request.EndTime));
                }
                endTime = parsedEnd;
            }

            targetStart = baseDate.Date.Add(startTime.ToTimeSpan());
            targetEnd = baseDate.Date.Add(endTime.ToTimeSpan());
        }
        else
        {
            targetStart = slot.StartsAt ?? DateTime.Today;
            targetEnd = slot.EndsAt ?? targetStart.AddMinutes(30);
        }

        if (targetEnd <= targetStart)
        {
            throw new ArgumentException("Giờ kết thúc phải sau giờ bắt đầu.");
        }

        // 2. Validate capacity & status
        int targetCapacity;
        if (request.Capacity.HasValue)
        {
            if (request.Capacity.Value < 1 || request.Capacity.Value > 100)
            {
                throw new ArgumentException("Sức chứa phải từ 1 đến 100 lượt.", nameof(request.Capacity));
            }

            if (request.Capacity.Value < activeBookings)
            {
                throw new ArgumentException($"Không thể giảm sức chứa xuống {request.Capacity.Value} vì đã có {activeBookings} lượt đặt chỗ hiện tại.", nameof(request.Capacity));
            }

            targetCapacity = request.Capacity.Value;
        }
        else
        {
            targetCapacity = slot.Capacity ?? 8;
        }

        string targetStatus;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var st = request.Status.Trim().ToUpperInvariant();
            if (st != "OPEN" && st != "CLOSED" && st != "FULL")
            {
                throw new ArgumentException("Trạng thái khung giờ không hợp lệ.", nameof(request.Status));
            }
            targetStatus = st;
        }
        else
        {
            targetStatus = string.IsNullOrWhiteSpace(slot.Status) ? "OPEN" : slot.Status.Trim().ToUpperInvariant();
        }

        // 3. Resolve & validate entities
        long? targetServiceId = slot.ServiceId;
        Service? service = slot.Service;
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

            targetServiceId = service.Id;
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

            targetServiceId = service.Id;
        }

        if (service != null && service.IsActive == false)
        {
            throw new ArgumentException("Dịch vụ khám đang bị tạm ngưng hoặc không hoạt động.");
        }

        long? targetDoctorId = slot.DoctorId;
        Doctor? doctor = slot.Doctor;
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

            targetDoctorId = doctor.Id;
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

            targetDoctorId = doctor.Id;
        }

        if (doctor != null && !doctor.IsActive)
        {
            throw new ArgumentException("Bác sĩ đang không ở trạng thái hoạt động.");
        }

        long? targetRoomId = slot.RoomId;
        HospitalRoom? room = slot.Room;
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

            targetRoomId = room.Id;
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

            targetRoomId = room.Id;
        }

        if (room != null && string.Equals(room.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Buồng khám đang ngừng hoạt động.");
        }

        // 4. Check conflicts on update (excluding current slot id)
        if (targetDoctorId.HasValue && targetStatus != "CLOSED")
        {
            var doctorConflict = await _db.ExaminationSlots.AsNoTracking()
                .AnyAsync(s => s.Id != id
                            && s.DoctorId == targetDoctorId.Value
                            && s.Status.ToUpper() != "CLOSED"
                            && s.StartsAt < targetEnd
                            && s.EndsAt > targetStart, ct);

            if (doctorConflict)
            {
                var docName = doctor?.Staff?.FullName ?? doctor?.DoctorCode ?? targetDoctorId.Value.ToString();
                throw new InvalidOperationException($"Xung đột lịch bác sĩ: Bác sĩ {docName} đã có ca khám trùng khoảng thời gian từ {targetStart:HH:mm} đến {targetEnd:HH:mm}.");
            }
        }

        if (targetRoomId.HasValue && targetStatus != "CLOSED")
        {
            var roomConflict = await _db.ExaminationSlots.AsNoTracking()
                .AnyAsync(s => s.Id != id
                            && s.RoomId == targetRoomId.Value
                            && s.Status.ToUpper() != "CLOSED"
                            && s.StartsAt < targetEnd
                            && s.EndsAt > targetStart, ct);

            if (roomConflict)
            {
                var roomName = room?.RoomCode ?? room?.Name ?? targetRoomId.Value.ToString();
                throw new InvalidOperationException($"Xung đột buồng khám: Phòng {roomName} đã được xếp lịch cho ca khám khác trong khoảng thời gian từ {targetStart:HH:mm} đến {targetEnd:HH:mm}.");
            }
        }

        // 5. Update slot entity
        slot.ServiceId = targetServiceId;
        slot.DoctorId = targetDoctorId;
        slot.RoomId = targetRoomId;
        slot.StartsAt = targetStart;
        slot.EndsAt = targetEnd;
        slot.Capacity = targetCapacity;
        slot.Status = targetStatus;

        await _db.SaveChangesAsync(ct);

        // 6. Map and return
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

        string displayStatus;
        if (targetStatus == "CLOSED")
        {
            displayStatus = "CLOSED";
        }
        else if (targetStatus == "FULL" || (targetCapacity > 0 && activeBookings >= targetCapacity))
        {
            displayStatus = "FULL";
        }
        else
        {
            displayStatus = "OPEN";
        }

        var needsReview = (targetCapacity > 0 && activeBookings > targetCapacity) ||
                          targetDoctorId == null ||
                          targetServiceId == null ||
                          targetRoomId == null ||
                          (doctor != null && !doctor.IsActive) ||
                          (service != null && service.IsActive == false) ||
                          (room != null && string.Equals(room.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase));

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
            CapacityBooked: activeBookings,
            CapacityMax: targetCapacity,
            Date: targetStart.ToString("yyyy-MM-dd"),
            DateDisplay: targetStart.ToString("dd/MM"),
            StartTime: targetStart.ToString("HH:mm"),
            EndTime: targetEnd.ToString("HH:mm"),
            Status: displayStatus,
            Facility: facility,
            NeedsReview: needsReview
        );
    }

    public async Task<CloseExaminationSlotResponseDto> CloseExaminationSlotAsync(
        long? actorUserId,
        string idOrCode,
        CloseExaminationSlotRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (request is null)
        {
            throw new ValidationException("Dữ liệu yêu cầu đóng khung giờ khám không hợp lệ.");
        }

        var slotId = ParseSlotIdentifier(idOrCode);
        var (normalizedReasonCode, effectiveReasonText, normalizedNote) = ValidateAndResolveCloseReason(
            request.ReasonCode,
            request.ReasonText,
            request.Reason,
            request.Note);
        var explicitAction = NormalizeAffectedBookingsActionOrThrow(request.AffectedBookingsAction);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var (isSystemAdmin, actorHospitalId) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        var slot = await LoadSlotWithDetailsAsync(slotId, ct)
            ?? throw new NotFoundException("ExaminationSlot", idOrCode.Trim());

        EnsureSlotHospitalOwnership(slot, isSystemAdmin, actorHospitalId);

        var previousStatus = (slot.Status ?? string.Empty).Trim().ToUpperInvariant();
        if (previousStatus == "CLOSED")
        {
            throw new ConflictException($"Khung giờ khám SLT-{slot.Id:D3} đã ở trạng thái đóng.");
        }

        var activeAppointments = await LoadActiveDependentAppointmentsAsync(slot.Id, ct);
        var activeBookingsCount = activeAppointments.Count;
        var hasDependentBookings = activeBookingsCount > 0;

        if (hasDependentBookings)
        {
            var isConfirmed = request.ConfirmUnderstood == true ||
                              (request.ConfirmUnderstood != false && !string.IsNullOrWhiteSpace(explicitAction));

            if (!isConfirmed)
            {
                throw new ConflictException(
                    $"Cảnh báo: Khung giờ SLT-{slot.Id:D3} đang có {activeBookingsCount} lượt bệnh nhân đã đặt hẹn. Vui lòng xác nhận cảnh báo (confirmUnderstood = true) và chọn phương án xử lý booking phụ thuộc (KEEP_EXISTING hoặc RESCHEDULE_REQUIRED) trước khi đóng khung giờ.");
            }
        }

        var effectiveAction = explicitAction ?? ActionKeepExisting;
        var closedAtIso = nowUtc.ToString("O", CultureInfo.InvariantCulture);
        var displayCloseReason = BuildDisplayCloseReason(normalizedReasonCode, effectiveReasonText, normalizedNote);

        var affectedSummaries = new List<DependentBookingSummaryDto>(activeAppointments.Count);
        foreach (var appt in activeAppointments)
        {
            if (effectiveAction == ActionRescheduleRequired && IsModifiableAppointmentStatus(appt.Status))
            {
                appt.ReceptionNote = AppendRescheduleRequiredNote(
                    appt.ReceptionNote,
                    slot.Id,
                    displayCloseReason);
            }

            affectedSummaries.Add(new DependentBookingSummaryDto(
                AppointmentId: appt.Id,
                AppointmentCode: string.IsNullOrWhiteSpace(appt.AppointmentCode) ? $"APT-{appt.Id:D4}" : appt.AppointmentCode,
                PatientId: appt.PatientId,
                PatientName: appt.Patient?.FullName ?? string.Empty,
                Status: string.IsNullOrWhiteSpace(appt.Status) ? "BOOKED" : appt.Status.Trim().ToUpperInvariant(),
                HandlingAction: effectiveAction));
        }

        slot.Status = "CLOSED";

        var slotHospitalId = ResolveSlotHospitalId(slot) ?? actorHospitalId;
        TryAddSlotAuditLog(
            actorUserId: actorUserId,
            hospitalId: slotHospitalId,
            action: SlotClosedAuditAction,
            slotId: slot.Id,
            reason: displayCloseReason,
            changeDataObject: new
            {
                slotCode = $"SLT-{slot.Id:D3}",
                previousStatus = string.IsNullOrWhiteSpace(previousStatus) ? "OPEN" : previousStatus,
                newStatus = "CLOSED",
                reasonCode = normalizedReasonCode,
                reasonText = effectiveReasonText,
                closeReason = displayCloseReason,
                note = normalizedNote,
                affectedBookingsAction = effectiveAction,
                affectedBookingsCount = activeBookingsCount,
                notifyDoctor = request.NotifyDoctor,
                notifyPatients = request.NotifyPatients,
                closedAt = closedAtIso,
            },
            ipAddress: ipAddress,
            occurredAt: nowUtc);

        await _db.SaveChangesAsync(ct);

        var closureMeta = new SlotClosureMetadata(
            ClosedAt: closedAtIso,
            CloseReason: displayCloseReason,
            CloseReasonCode: normalizedReasonCode,
            CloseNote: normalizedNote,
            AffectedBookingsAction: effectiveAction);

        var itemDto = MapSlotToItemDto(slot, activeBookingsCount, closureMeta);
        var warningMessage = hasDependentBookings
            ? effectiveAction == ActionRescheduleRequired
                ? $"Khung giờ SLT-{slot.Id:D3} có {activeBookingsCount} lượt đặt hẹn phụ thuộc đã được gắn cờ yêu cầu liên hệ dời lịch (RESCHEDULE_REQUIRED)."
                : $"Khung giờ SLT-{slot.Id:D3} có {activeBookingsCount} lượt đặt hẹn phụ thuộc được giữ nguyên lịch khám (KEEP_EXISTING)."
            : null;

        var message = hasDependentBookings
            ? $"Đã đóng khung giờ SLT-{slot.Id:D3} thành công ({activeBookingsCount} lượt đặt hẹn: {effectiveAction})."
            : "Khung giờ khám đã được đóng thành công.";

        return BuildCloseSlotResponse(
            itemDto,
            message: message,
            hasDependentBookings: hasDependentBookings,
            affectedBookingsCount: activeBookingsCount,
            warningMessage: warningMessage,
            notifyDoctor: request.NotifyDoctor,
            notifyPatients: request.NotifyPatients,
            affectedBookings: affectedSummaries);
    }

    public async Task<BatchCloseExaminationSlotsResponseDto> BatchCloseExaminationSlotsAsync(
        long? actorUserId,
        BatchCloseExaminationSlotsRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (request is null)
        {
            throw new ValidationException("Dữ liệu yêu cầu đóng hàng loạt khung giờ khám không hợp lệ.");
        }

        var targetSlotIds = new List<long>();
        if (request.SlotIds is not null)
        {
            foreach (var rawId in request.SlotIds)
            {
                if (rawId <= 0)
                {
                    throw new ValidationException("Mã định danh khung giờ khám không hợp lệ.");
                }

                if (!targetSlotIds.Contains(rawId))
                {
                    targetSlotIds.Add(rawId);
                }
            }
        }

        if (request.Ids is not null)
        {
            foreach (var rawIdOrCode in request.Ids)
            {
                var parsed = ParseSlotIdentifier(rawIdOrCode);
                if (!targetSlotIds.Contains(parsed))
                {
                    targetSlotIds.Add(parsed);
                }
            }
        }

        if (targetSlotIds.Count == 0)
        {
            throw new ValidationException("Vui lòng chọn ít nhất một khung giờ khám để đóng.");
        }

        var (normalizedReasonCode, effectiveReasonText, normalizedNote) = ValidateAndResolveCloseReason(
            request.ReasonCode,
            request.ReasonText,
            request.Reason,
            request.Note);
        var explicitAction = NormalizeAffectedBookingsActionOrThrow(request.AffectedBookingsAction);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var (isSystemAdmin, actorHospitalId) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        var slots = await _db.ExaminationSlots
            .Include(s => s.Doctor).ThenInclude(d => d!.Staff)
            .Include(s => s.Doctor).ThenInclude(d => d!.Specialty)
            .Include(s => s.Service).ThenInclude(srv => srv!.Specialty)
            .Include(s => s.Service).ThenInclude(srv => srv!.Department)
            .Include(s => s.Room).ThenInclude(r => r!.Department)
            .Where(s => targetSlotIds.Contains(s.Id))
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        if (slots.Count != targetSlotIds.Count)
        {
            var foundIds = slots.Select(s => s.Id).ToHashSet();
            var missingId = targetSlotIds.First(id => !foundIds.Contains(id));
            throw new NotFoundException("ExaminationSlot", missingId);
        }

        foreach (var slot in slots)
        {
            EnsureSlotHospitalOwnership(slot, isSystemAdmin, actorHospitalId);
        }

        var activeAppointments = await _db.Appointments
            .Where(a =>
                a.SlotId != null &&
                targetSlotIds.Contains(a.SlotId.Value) &&
                (a.Status == null || (a.Status.ToUpper() != "CANCELLED" && a.Status.ToUpper() != "NO_SHOW")))
            .ToListAsync(ct);

        var totalActiveBookings = activeAppointments.Count;
        if (totalActiveBookings > 0)
        {
            var isConfirmed = request.ConfirmUnderstood == true ||
                              (request.ConfirmUnderstood != false && !string.IsNullOrWhiteSpace(explicitAction));

            if (!isConfirmed)
            {
                throw new ConflictException(
                    $"Cảnh báo: Có tổng cộng {totalActiveBookings} lượt đặt hẹn phụ thuộc trong các khung giờ đã chọn. Vui lòng xác nhận cảnh báo (confirmUnderstood = true) và chọn phương án xử lý trước khi đóng hàng loạt.");
            }
        }

        var effectiveAction = explicitAction ?? ActionKeepExisting;
        var closedAtIso = nowUtc.ToString("O", CultureInfo.InvariantCulture);
        var displayCloseReason = BuildDisplayCloseReason(normalizedReasonCode, effectiveReasonText, normalizedNote);

        var appointmentsBySlotId = activeAppointments
            .Where(a => a.SlotId.HasValue)
            .GroupBy(a => a.SlotId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var closureMeta = new SlotClosureMetadata(
            ClosedAt: closedAtIso,
            CloseReason: displayCloseReason,
            CloseReasonCode: normalizedReasonCode,
            CloseNote: normalizedNote,
            AffectedBookingsAction: effectiveAction);

        var updatedDtos = new List<ExaminationSlotAdminItemDto>(slots.Count);
        foreach (var slot in slots)
        {
            var previousStatus = (slot.Status ?? string.Empty).Trim().ToUpperInvariant();
            appointmentsBySlotId.TryGetValue(slot.Id, out var slotAppointments);
            var slotBookedCount = slotAppointments?.Count ?? 0;

            if (effectiveAction == ActionRescheduleRequired && slotAppointments is not null)
            {
                foreach (var appt in slotAppointments)
                {
                    if (IsModifiableAppointmentStatus(appt.Status))
                    {
                        appt.ReceptionNote = AppendRescheduleRequiredNote(
                            appt.ReceptionNote,
                            slot.Id,
                            displayCloseReason);
                    }
                }
            }

            slot.Status = "CLOSED";

            var slotHospitalId = ResolveSlotHospitalId(slot) ?? actorHospitalId;
            TryAddSlotAuditLog(
                actorUserId: actorUserId,
                hospitalId: slotHospitalId,
                action: SlotClosedAuditAction,
                slotId: slot.Id,
                reason: displayCloseReason,
                changeDataObject: new
                {
                    slotCode = $"SLT-{slot.Id:D3}",
                    previousStatus = string.IsNullOrWhiteSpace(previousStatus) ? "OPEN" : previousStatus,
                    newStatus = "CLOSED",
                    reasonCode = normalizedReasonCode,
                    reasonText = effectiveReasonText,
                    closeReason = displayCloseReason,
                    note = normalizedNote,
                    affectedBookingsAction = effectiveAction,
                    affectedBookingsCount = slotBookedCount,
                    notifyDoctor = request.NotifyDoctor,
                    notifyPatients = request.NotifyPatients,
                    closedAt = closedAtIso,
                    batchClose = true,
                },
                ipAddress: ipAddress,
                occurredAt: nowUtc);

            updatedDtos.Add(MapSlotToItemDto(slot, slotBookedCount, closureMeta));
        }

        await _db.SaveChangesAsync(ct);

        return new BatchCloseExaminationSlotsResponseDto(
            Success: true,
            Count: updatedDtos.Count,
            TotalAffectedBookings: totalActiveBookings,
            Message: $"Đã đóng thành công {updatedDtos.Count} khung giờ khám được chọn.",
            Slots: updatedDtos);
    }

    public async Task<CloseExaminationSlotResponseDto> ReopenExaminationSlotAsync(
        long? actorUserId,
        string idOrCode,
        ReopenExaminationSlotRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        var slotId = ParseSlotIdentifier(idOrCode);

        string? normalizedNote = null;
        var rawNote = request?.Note ?? request?.Reason;
        if (!string.IsNullOrWhiteSpace(rawNote))
        {
            normalizedNote = rawNote.Trim();
            if (normalizedNote.Length > MaxNoteLength)
            {
                throw new ValidationException($"Ghi chú mở lại khung giờ không được vượt quá {MaxNoteLength} ký tự.");
            }
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var (isSystemAdmin, actorHospitalId) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        var slot = await LoadSlotWithDetailsAsync(slotId, ct)
            ?? throw new NotFoundException("ExaminationSlot", idOrCode.Trim());

        EnsureSlotHospitalOwnership(slot, isSystemAdmin, actorHospitalId);

        var previousStatus = (slot.Status ?? string.Empty).Trim().ToUpperInvariant();
        if (previousStatus != "CLOSED")
        {
            throw new ConflictException($"Khung giờ khám SLT-{slot.Id:D3} đang ở trạng thái hoạt động, không cần mở lại.");
        }

        if (slot.Service is not null && slot.Service.IsActive == false)
        {
            throw new ConflictException("Không thể mở lại khung giờ vì dịch vụ khám đang tạm ngưng hoạt động.");
        }

        if (slot.Doctor is not null && !slot.Doctor.IsActive)
        {
            throw new ConflictException("Không thể mở lại khung giờ vì bác sĩ phụ trách đang ngừng hoạt động.");
        }

        if (slot.Room is not null && string.Equals(slot.Room.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Không thể mở lại khung giờ vì buồng khám đang ngừng hoạt động.");
        }

        if (slot.StartsAt.HasValue && slot.EndsAt.HasValue)
        {
            var start = slot.StartsAt.Value;
            var end = slot.EndsAt.Value;

            if (slot.DoctorId.HasValue)
            {
                var doctorConflict = await _db.ExaminationSlots.AsNoTracking()
                    .AnyAsync(
                        s => s.Id != slot.Id &&
                             s.DoctorId == slot.DoctorId.Value &&
                             s.Status.ToUpper() != "CLOSED" &&
                             s.StartsAt < end &&
                             s.EndsAt > start,
                        ct);

                if (doctorConflict)
                {
                    var docName = slot.Doctor?.Staff?.FullName ?? slot.Doctor?.DoctorCode ?? slot.DoctorId.Value.ToString(CultureInfo.InvariantCulture);
                    throw new ConflictException(
                        $"Xung đột lịch bác sĩ: Bác sĩ {docName} đã có ca khám đang mở trùng khoảng thời gian từ {start:HH:mm} đến {end:HH:mm}.");
                }
            }

            if (slot.RoomId.HasValue)
            {
                var roomConflict = await _db.ExaminationSlots.AsNoTracking()
                    .AnyAsync(
                        s => s.Id != slot.Id &&
                             s.RoomId == slot.RoomId.Value &&
                             s.Status.ToUpper() != "CLOSED" &&
                             s.StartsAt < end &&
                             s.EndsAt > start,
                        ct);

                if (roomConflict)
                {
                    var roomName = slot.Room?.RoomCode ?? slot.Room?.Name ?? slot.RoomId.Value.ToString(CultureInfo.InvariantCulture);
                    throw new ConflictException(
                        $"Xung đột buồng khám: Phòng {roomName} đã được xếp lịch cho ca khám khác đang mở trong khoảng thời gian từ {start:HH:mm} đến {end:HH:mm}.");
                }
            }
        }

        var activeAppointments = await LoadActiveDependentAppointmentsAsync(slot.Id, ct);
        var activeBookingsCount = activeAppointments.Count;
        var maxCap = slot.Capacity ?? 0;
        var reopenedStatus = maxCap > 0 && activeBookingsCount >= maxCap ? "FULL" : "OPEN";

        slot.Status = reopenedStatus;

        var slotHospitalId = ResolveSlotHospitalId(slot) ?? actorHospitalId;
        var reopenReason = normalizedNote ?? "Mở lại khung giờ khám cho phép tiếp nhận đặt lịch";
        TryAddSlotAuditLog(
            actorUserId: actorUserId,
            hospitalId: slotHospitalId,
            action: SlotReopenedAuditAction,
            slotId: slot.Id,
            reason: reopenReason,
            changeDataObject: new
            {
                slotCode = $"SLT-{slot.Id:D3}",
                previousStatus = previousStatus,
                newStatus = reopenedStatus,
                note = normalizedNote,
                activeBookingsCount = activeBookingsCount,
                reopenedAt = nowUtc.ToString("O", CultureInfo.InvariantCulture),
            },
            ipAddress: ipAddress,
            occurredAt: nowUtc);

        await _db.SaveChangesAsync(ct);

        var itemDto = MapSlotToItemDto(slot, activeBookingsCount, closureMeta: null);
        return BuildCloseSlotResponse(
            itemDto,
            message: "Khung giờ khám đã được mở lại thành công.",
            hasDependentBookings: activeBookingsCount > 0,
            affectedBookingsCount: activeBookingsCount,
            warningMessage: null,
            notifyDoctor: false,
            notifyPatients: false,
            affectedBookings: null);
    }

    private sealed record SlotClosureMetadata(
        string? ClosedAt,
        string? CloseReason,
        string? CloseReasonCode,
        string? CloseNote,
        string? AffectedBookingsAction);

    private static ExaminationSlotAdminItemDto MapSlotToItemDto(
        ExaminationSlot slot,
        int booked,
        SlotClosureMetadata? closureMeta)
    {
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

        var isClosed = displayStatus == "CLOSED";
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
            NeedsReview: needsReview,
            ClosedAt: isClosed ? closureMeta?.ClosedAt : null,
            CloseReason: isClosed ? closureMeta?.CloseReason : null,
            CloseReasonCode: isClosed ? closureMeta?.CloseReasonCode : null,
            CloseNote: isClosed ? closureMeta?.CloseNote : null,
            AffectedBookingsAction: isClosed ? closureMeta?.AffectedBookingsAction : null
        );
    }

    private static CloseExaminationSlotResponseDto BuildCloseSlotResponse(
        ExaminationSlotAdminItemDto item,
        string message,
        bool hasDependentBookings,
        int affectedBookingsCount,
        string? warningMessage,
        bool notifyDoctor,
        bool notifyPatients,
        IReadOnlyList<DependentBookingSummaryDto>? affectedBookings) =>
        new(
            Id: item.Id,
            Code: item.Code,
            DoctorId: item.DoctorId,
            DoctorName: item.DoctorName,
            ServiceId: item.ServiceId,
            ServiceName: item.ServiceName,
            ServiceCode: item.ServiceCode,
            ServiceType: item.ServiceType,
            Specialty: item.Specialty,
            RoomId: item.RoomId,
            Room: item.Room,
            CapacityBooked: item.CapacityBooked,
            CapacityMax: item.CapacityMax,
            Date: item.Date,
            DateDisplay: item.DateDisplay,
            StartTime: item.StartTime,
            EndTime: item.EndTime,
            Status: item.Status,
            Facility: item.Facility,
            NeedsReview: item.NeedsReview,
            ClosedAt: item.ClosedAt,
            CloseReason: item.CloseReason,
            CloseReasonCode: item.CloseReasonCode,
            CloseNote: item.CloseNote,
            AffectedBookingsAction: item.AffectedBookingsAction,
            Success: true,
            Message: message,
            HasDependentBookings: hasDependentBookings,
            AffectedBookingsCount: affectedBookingsCount,
            WarningMessage: warningMessage,
            NotifyDoctor: notifyDoctor,
            NotifyPatients: notifyPatients,
            AffectedBookings: affectedBookings,
            Slot: item);

    private static long ParseSlotIdentifier(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã định danh khung giờ khám không được để trống.");
        }

        var trimmed = idOrCode.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId))
        {
            if (numericId <= 0)
            {
                throw new ValidationException("Mã khung giờ khám phải là số nguyên dương.");
            }

            return numericId;
        }

        if (trimmed.StartsWith("SLT-", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = trimmed["SLT-".Length..];
            var lastDash = suffix.LastIndexOf('-');
            var candidateNum = lastDash >= 0 ? suffix[(lastDash + 1)..] : suffix;
            if (long.TryParse(candidateNum, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFromCode) &&
                parsedFromCode > 0)
            {
                return parsedFromCode;
            }
        }

        throw new ValidationException("Mã định danh khung giờ khám không hợp lệ.");
    }

    private static (string ReasonCode, string ReasonText, string? Note) ValidateAndResolveCloseReason(
        string? rawReasonCode,
        string? rawReasonText,
        string? rawReason,
        string? rawNote)
    {
        string? normalizedNote = null;
        if (!string.IsNullOrWhiteSpace(rawNote))
        {
            normalizedNote = rawNote.Trim();
            if (normalizedNote.Length > MaxNoteLength)
            {
                throw new ValidationException($"Ghi chú đóng khung giờ không được vượt quá {MaxNoteLength} ký tự.");
            }
        }

        string normalizedReasonCode;
        if (!string.IsNullOrWhiteSpace(rawReasonCode))
        {
            var candidate = rawReasonCode.Trim().ToUpperInvariant();
            if (!CloseReasonLabels.ContainsKey(candidate))
            {
                throw new ValidationException(
                    "Mã lý do đóng khung giờ không hợp lệ. Giá trị hợp lệ: DOCTOR_UNAVAILABLE, EQUIPMENT_MAINTENANCE, SCHEDULE_ADJUSTMENT, EMERGENCY, OTHER.");
            }

            normalizedReasonCode = candidate;
        }
        else if (!string.IsNullOrWhiteSpace(rawReason) && CloseReasonLabels.ContainsKey(rawReason.Trim()))
        {
            normalizedReasonCode = rawReason.Trim().ToUpperInvariant();
        }
        else if (!string.IsNullOrWhiteSpace(rawReason) || !string.IsNullOrWhiteSpace(rawReasonText) || !string.IsNullOrWhiteSpace(normalizedNote))
        {
            normalizedReasonCode = "DOCTOR_UNAVAILABLE";
        }
        else
        {
            throw new ValidationException("Vui lòng chọn lý do đóng khung giờ khám.");
        }

        var defaultLabel = CloseReasonLabels[normalizedReasonCode];
        var customText = !string.IsNullOrWhiteSpace(rawReasonText)
            ? rawReasonText.Trim()
            : !string.IsNullOrWhiteSpace(rawReason) && !CloseReasonLabels.ContainsKey(rawReason.Trim())
                ? rawReason.Trim()
                : null;

        if (customText is not null && customText.Length > MaxNoteLength)
        {
            throw new ValidationException($"Nội dung lý do đóng khung giờ không được vượt quá {MaxNoteLength} ký tự.");
        }

        if (normalizedReasonCode == "OTHER")
        {
            var otherDetail = normalizedNote
                ?? (customText is not null && !string.Equals(customText, defaultLabel, StringComparison.OrdinalIgnoreCase)
                    ? customText
                    : null);

            if (string.IsNullOrWhiteSpace(otherDetail) || otherDetail.Trim().Length < MinOtherNoteLength)
            {
                throw new ValidationException("Vui lòng nhập chi tiết lý do đóng (tối thiểu 5 ký tự).");
            }

            normalizedNote ??= otherDetail.Trim();
        }

        var effectiveReasonText = customText ?? defaultLabel;
        return (normalizedReasonCode, effectiveReasonText, normalizedNote);
    }

    private static string? NormalizeAffectedBookingsActionOrThrow(string? rawAction)
    {
        if (string.IsNullOrWhiteSpace(rawAction))
        {
            return null;
        }

        var normalized = rawAction.Trim().ToUpperInvariant();
        if (normalized != ActionKeepExisting && normalized != ActionRescheduleRequired)
        {
            throw new ValidationException(
                "Phương án xử lý lịch hẹn phụ thuộc không hợp lệ. Chỉ chấp nhận KEEP_EXISTING hoặc RESCHEDULE_REQUIRED.");
        }

        return normalized;
    }

    private static string BuildDisplayCloseReason(string reasonCode, string reasonText, string? note)
    {
        if (reasonCode == "OTHER" && !string.IsNullOrWhiteSpace(note))
        {
            return note;
        }

        if (!string.IsNullOrWhiteSpace(note) &&
            !string.Equals(reasonText, note, StringComparison.OrdinalIgnoreCase))
        {
            return $"{reasonText} ({note})";
        }

        return reasonText;
    }

    private static bool IsModifiableAppointmentStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        var upper = status.Trim().ToUpperInvariant();
        return upper is "BOOKED" or "CONFIRMED" or "PENDING";
    }

    private static string AppendRescheduleRequiredNote(string? existingNote, long slotId, string closeReason)
    {
        var marker = $"[RESCHEDULE_REQUIRED] Khung giờ SLT-{slotId:D3} đã đóng ({closeReason}) — cần liên hệ bệnh nhân dời lịch.";
        if (string.IsNullOrWhiteSpace(existingNote))
        {
            return marker;
        }

        if (existingNote.Contains("[RESCHEDULE_REQUIRED]", StringComparison.OrdinalIgnoreCase))
        {
            return existingNote;
        }

        return $"{existingNote.Trim()} | {marker}";
    }

    private async Task<ExaminationSlot?> LoadSlotWithDetailsAsync(long slotId, CancellationToken ct) =>
        await _db.ExaminationSlots
            .Include(s => s.Doctor).ThenInclude(d => d!.Staff)
            .Include(s => s.Doctor).ThenInclude(d => d!.Specialty)
            .Include(s => s.Service).ThenInclude(srv => srv!.Specialty)
            .Include(s => s.Service).ThenInclude(srv => srv!.Department)
            .Include(s => s.Room).ThenInclude(r => r!.Department)
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);

    private async Task<List<Appointment>> LoadActiveDependentAppointmentsAsync(long slotId, CancellationToken ct)
    {
        try
        {
            return await _db.Appointments
                .Include(a => a.Patient)
                .Where(a =>
                    a.SlotId == slotId &&
                    (a.Status == null || (a.Status.ToUpper() != "CANCELLED" && a.Status.ToUpper() != "NO_SHOW")))
                .OrderBy(a => a.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return await _db.Appointments
                .Where(a =>
                    a.SlotId == slotId &&
                    (a.Status == null || (a.Status.ToUpper() != "CANCELLED" && a.Status.ToUpper() != "NO_SHOW")))
                .OrderBy(a => a.Id)
                .ToListAsync(ct);
        }
    }

    private static long? ResolveSlotHospitalId(ExaminationSlot slot) =>
        slot.Room?.HospitalId
        ?? slot.Room?.Department?.HospitalId
        ?? slot.Doctor?.HospitalId
        ?? slot.Doctor?.Staff?.HospitalId
        ?? slot.Service?.HospitalId
        ?? slot.Service?.Department?.HospitalId;

    private static void EnsureSlotHospitalOwnership(
        ExaminationSlot slot,
        bool isSystemAdmin,
        long? actorHospitalId)
    {
        if (isSystemAdmin || !actorHospitalId.HasValue)
        {
            return;
        }

        var slotHospitalId = ResolveSlotHospitalId(slot);
        if (slotHospitalId.HasValue && slotHospitalId.Value != actorHospitalId.Value)
        {
            throw new ForbiddenException("Bạn không có quyền thao tác khung giờ khám thuộc cơ sở y tế khác.");
        }
    }

    private async Task<(bool IsSystemAdmin, long? ActorHospitalId)> EnsureAdminAccessAsync(
        long? actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (!actorUserId.HasValue)
        {
            return (true, null);
        }

        if (actorUserId.Value <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        try
        {
            var hasAnyUsers = await _db.Users.AsNoTracking().AnyAsync(ct);
            if (!hasAnyUsers)
            {
                return (true, null);
            }

            var actorUser = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == actorUserId.Value, ct)
                ?? throw new NotFoundException("User", actorUserId.Value);

            if (actorUser.AccountStatus != AccountStatus.ACTIVE)
            {
                throw new ForbiddenException("Tài khoản quản trị không ở trạng thái hoạt động.");
            }

            if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
            {
                throw new ForbiddenException("Tài khoản quản trị đang bị tạm khóa.");
            }

            var actorRoleIds = await _db.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == actorUserId.Value)
                .Select(ur => ur.RoleId)
                .ToListAsync(ct);

            var actorRoleCodes = await _db.Roles
                .AsNoTracking()
                .Where(r => actorRoleIds.Contains(r.Id))
                .Select(r => r.Code)
                .ToListAsync(ct);

            var isSystemAdmin = actorRoleCodes.Any(c =>
                string.Equals(c, "SYSTEM_ADMIN", StringComparison.OrdinalIgnoreCase));
            var isHospitalAdmin = actorRoleCodes.Any(c =>
                string.Equals(c, "HOSPITAL_ADMIN", StringComparison.OrdinalIgnoreCase));

            if (!isSystemAdmin && !isHospitalAdmin)
            {
                throw new ForbiddenException("Người dùng không có quyền quản trị khung giờ khám.");
            }

            var actorStaff = await _db.Staff
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == actorUserId.Value, ct);

            if (actorStaff is not null &&
                !string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                throw new ForbiddenException("Hồ sơ nhân sự của quản trị viên không ở trạng thái hoạt động.");
            }

            if (!isSystemAdmin)
            {
                if (actorStaff is null || !actorStaff.HospitalId.HasValue)
                {
                    throw new ForbiddenException("Tài khoản quản trị bệnh viện chưa được gán cơ sở y tế.");
                }

                var hospital = await _db.Hospitals
                    .AsNoTracking()
                    .FirstOrDefaultAsync(h => h.Id == actorStaff.HospitalId.Value, ct);

                if (hospital is not null && hospital.IsActive == false)
                {
                    throw new ForbiddenException("Cơ sở y tế của quản trị viên đang ngừng hoạt động.");
                }
            }

            return (isSystemAdmin, actorStaff?.HospitalId);
        }
        catch (NotSupportedException)
        {
            return (true, null);
        }
    }

    private void TryAddSlotAuditLog(
        long? actorUserId,
        long? hospitalId,
        string action,
        long slotId,
        string reason,
        object changeDataObject,
        string? ipAddress,
        DateTime occurredAt)
    {
        try
        {
            var changeData = JsonSerializer.Serialize(changeDataObject);
            _db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                HospitalId = hospitalId,
                Action = action,
                EntityType = SlotAuditEntityType,
                EntityId = slotId.ToString(CultureInfo.InvariantCulture),
                Reason = reason,
                ChangeData = changeData,
                IpAddress = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var parsedIp)
                    ? parsedIp
                    : null,
                OccurredAt = occurredAt,
            });
        }
        catch (NotSupportedException)
        {
            // Ignored when minimal test DbContext does not configure AuditLogs DbSet
        }
    }

    private async Task<Dictionary<long, SlotClosureMetadata>> TryLoadClosureMetadataMapAsync(
        IReadOnlyList<long> closedSlotIds,
        CancellationToken ct)
    {
        var result = new Dictionary<long, SlotClosureMetadata>();
        if (closedSlotIds.Count == 0)
        {
            return result;
        }

        try
        {
            var entityIdStrings = closedSlotIds
                .Select(id => id.ToString(CultureInfo.InvariantCulture))
                .ToList();

            var logs = await _db.AuditLogs
                .AsNoTracking()
                .Where(a =>
                    a.EntityType == SlotAuditEntityType &&
                    a.EntityId != null &&
                    entityIdStrings.Contains(a.EntityId) &&
                    (a.Action == SlotClosedAuditAction || a.Action == SlotReopenedAuditAction))
                .OrderByDescending(a => a.OccurredAt)
                .ThenByDescending(a => a.Id)
                .ToListAsync(ct);

            foreach (var group in logs.GroupBy(l => l.EntityId!))
            {
                if (!long.TryParse(group.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slotId))
                {
                    continue;
                }

                var latest = group.First();
                if (latest.Action != SlotClosedAuditAction)
                {
                    continue;
                }

                result[slotId] = ParseClosureMetadataFromAuditLog(latest);
            }
        }
        catch (NotSupportedException)
        {
            // Ignored when minimal test DbContext does not configure AuditLogs DbSet
        }

        return result;
    }

    private static SlotClosureMetadata ParseClosureMetadataFromAuditLog(AuditLog log)
    {
        string? closedAt = log.OccurredAt.ToString("O", CultureInfo.InvariantCulture);
        string? closeReason = log.Reason;
        string? closeReasonCode = null;
        string? closeNote = null;
        string? affectedBookingsAction = null;

        if (!string.IsNullOrWhiteSpace(log.ChangeData))
        {
            try
            {
                using var doc = JsonDocument.Parse(log.ChangeData);
                var root = doc.RootElement;

                if (root.TryGetProperty("closedAt", out var closedAtEl) && closedAtEl.ValueKind == JsonValueKind.String)
                {
                    closedAt = closedAtEl.GetString() ?? closedAt;
                }

                if (root.TryGetProperty("closeReason", out var closeReasonEl) && closeReasonEl.ValueKind == JsonValueKind.String)
                {
                    closeReason = closeReasonEl.GetString() ?? closeReason;
                }
                else if (root.TryGetProperty("reasonText", out var reasonTextEl) && reasonTextEl.ValueKind == JsonValueKind.String)
                {
                    closeReason ??= reasonTextEl.GetString();
                }

                if (root.TryGetProperty("reasonCode", out var reasonCodeEl) && reasonCodeEl.ValueKind == JsonValueKind.String)
                {
                    closeReasonCode = reasonCodeEl.GetString();
                }

                if (root.TryGetProperty("note", out var noteEl) && noteEl.ValueKind == JsonValueKind.String)
                {
                    closeNote = noteEl.GetString();
                }

                if (root.TryGetProperty("affectedBookingsAction", out var actionEl) && actionEl.ValueKind == JsonValueKind.String)
                {
                    affectedBookingsAction = actionEl.GetString();
                }
            }
            catch (JsonException)
            {
                // Keep fallback metadata from log columns
            }
        }

        return new SlotClosureMetadata(
            ClosedAt: closedAt,
            CloseReason: closeReason,
            CloseReasonCode: closeReasonCode,
            CloseNote: closeNote,
            AffectedBookingsAction: affectedBookingsAction);
    }
}

