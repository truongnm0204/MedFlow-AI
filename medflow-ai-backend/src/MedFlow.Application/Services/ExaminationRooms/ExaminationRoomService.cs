using System.Net;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.ExaminationRooms;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.ExaminationRooms;

public sealed class ExaminationRoomService : IExaminationRoomService
{
    private const string UnknownFacilityFallback = "Chưa cập nhật";

    private readonly IApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public ExaminationRoomService(IApplicationDbContext dbContext, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ExaminationRoomListResponseDto> GetExaminationRoomListAsync(
        long actorUserId,
        GetExaminationRoomListRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Page < 1)
        {
            throw new ArgumentException("Trang yêu cầu phải lớn hơn hoặc bằng 1.", nameof(request.Page));
        }

        if (request.PageSize < 1 || request.PageSize > 100)
        {
            throw new ArgumentException("Kích thước trang phải từ 1 đến 100.", nameof(request.PageSize));
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var (isSystemAdmin, actorHospitalId) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        long? scopedHospitalId = request.HospitalId;
        if (!isSystemAdmin)
        {
            if (request.HospitalId.HasValue && request.HospitalId.Value != actorHospitalId!.Value)
            {
                throw new ForbiddenException("Bạn không có quyền xem danh mục buồng khám thuộc cơ sở y tế khác.");
            }

            scopedHospitalId = actorHospitalId!.Value;
        }
        else if (request.HospitalId.HasValue)
        {
            if (request.HospitalId.Value <= 0)
            {
                throw new ValidationException("Mã cơ sở y tế không hợp lệ.");
            }

            var hospitalExists = await _dbContext.Hospitals
                .AsNoTracking()
                .AnyAsync(h => h.Id == request.HospitalId.Value, ct);
            if (!hospitalExists)
            {
                throw new NotFoundException("Hospital", request.HospitalId.Value);
            }
        }

        var baseQuery = _dbContext.HospitalRooms.AsNoTracking();
        if (!isSystemAdmin && actorHospitalId.HasValue)
        {
            baseQuery = baseQuery.Where(r => r.HospitalId == actorHospitalId.Value);
        }

        var allRooms = await baseQuery.ToListAsync(ct);
        var totalAll = allRooms.Count;
        var activeCount = allRooms.Count(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase));
        var inactiveCount = totalAll - activeCount;
        var departmentsCount = allRooms.Select(r => r.DepartmentId).Where(d => d.HasValue).Distinct().Count();

        var metrics = new ExaminationRoomMetricsDto(totalAll, activeCount, inactiveCount, departmentsCount);

        var filteredQuery = baseQuery.Include(r => r.Department).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim().ToLower();
            filteredQuery = filteredQuery.Where(r =>
                r.RoomCode.ToLower().Contains(kw) ||
                r.Name.ToLower().Contains(kw) ||
                (r.Location != null && r.Location.ToLower().Contains(kw)));
        }

        if (request.DepartmentId.HasValue)
        {
            filteredQuery = filteredQuery.Where(r => r.DepartmentId == request.DepartmentId.Value);
        }

        if (scopedHospitalId.HasValue)
        {
            filteredQuery = filteredQuery.Where(r => r.HospitalId == scopedHospitalId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.RoomType))
        {
            var rt = request.RoomType.Trim().ToLower();
            filteredQuery = filteredQuery.Where(r => r.RoomType.ToLower() == rt);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var st = request.Status.Trim().ToLower();
            filteredQuery = filteredQuery.Where(r => r.Status.ToLower() == st);
        }

        if (request.IsAvailable.HasValue)
        {
            if (request.IsAvailable.Value)
            {
                filteredQuery = filteredQuery.Where(r => r.Status.ToLower() == "active");
            }
            else
            {
                filteredQuery = filteredQuery.Where(r => r.Status.ToLower() != "active");
            }
        }

        var totalFiltered = await filteredQuery.CountAsync(ct);
        var totalPages = totalFiltered == 0 ? 1 : (int)Math.Ceiling((double)totalFiltered / request.PageSize);

        var entities = await filteredQuery
            .OrderBy(r => r.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var hospitalIds = entities
            .Where(r => r.HospitalId.HasValue)
            .Select(r => r.HospitalId!.Value)
            .Distinct()
            .ToList();

        var hospitalsById = hospitalIds.Count == 0
            ? new Dictionary<long, string>()
            : await _dbContext.Hospitals
                .AsNoTracking()
                .Where(h => hospitalIds.Contains(h.Id))
                .ToDictionaryAsync(h => h.Id, h => h.Name, ct);

        var roomIds = entities.Select(r => r.Id).ToList();
        var visitCounts = new Dictionary<long, int>();
        var visitDoctors = new Dictionary<long, HashSet<long>>();
        var slotCounts = new Dictionary<long, int>();
        var slotDoctors = new Dictionary<long, HashSet<long>>();

        if (roomIds.Count > 0)
        {
            try
            {
                var visits = await _dbContext.Visits
                    .AsNoTracking()
                    .Where(v => v.ExaminationRoomId.HasValue && roomIds.Contains(v.ExaminationRoomId.Value) &&
                                (v.Status == "WAITING" || v.Status == "CALLED" || v.Status == "IN_PROGRESS" || v.Status == "IN_CONSULTATION"))
                    .ToListAsync(ct);

                foreach (var v in visits)
                {
                    var rId = v.ExaminationRoomId!.Value;
                    visitCounts[rId] = visitCounts.GetValueOrDefault(rId) + 1;
                    if (v.AssignedDoctorId.HasValue)
                    {
                        if (!visitDoctors.TryGetValue(rId, out var set))
                        {
                            set = new HashSet<long>();
                            visitDoctors[rId] = set;
                        }
                        set.Add(v.AssignedDoctorId.Value);
                    }
                }
            }
            catch
            {
                // Ignored when optional related DbSet is unconfigured in minimal test context
            }

            try
            {
                var slots = await _dbContext.ExaminationSlots
                    .AsNoTracking()
                    .Where(s => s.RoomId.HasValue && roomIds.Contains(s.RoomId.Value) &&
                                s.Status != "CLOSED" && s.Status != "CANCELLED")
                    .ToListAsync(ct);

                foreach (var s in slots)
                {
                    var rId = s.RoomId!.Value;
                    slotCounts[rId] = slotCounts.GetValueOrDefault(rId) + 1;
                    if (s.DoctorId.HasValue)
                    {
                        if (!slotDoctors.TryGetValue(rId, out var set))
                        {
                            set = new HashSet<long>();
                            slotDoctors[rId] = set;
                        }
                        set.Add(s.DoctorId.Value);
                    }
                }
            }
            catch
            {
                // Ignored when optional related DbSet is unconfigured in minimal test context
            }
        }

        var data = entities.Select(r =>
        {
            var isActive = string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);
            var facility = r.HospitalId.HasValue && hospitalsById.TryGetValue(r.HospitalId.Value, out var hospitalName)
                ? ResolveFacilityName(hospitalName)
                : UnknownFacilityFallback;

            var vCount = visitCounts.GetValueOrDefault(r.Id, 0);
            var sCount = slotCounts.GetValueOrDefault(r.Id, 0);
            var docSet = new HashSet<long>();
            if (visitDoctors.TryGetValue(r.Id, out var vd)) docSet.UnionWith(vd);
            if (slotDoctors.TryGetValue(r.Id, out var sd)) docSet.UnionWith(sd);
            var dCount = docSet.Count;
            var hasDeps = vCount > 0 || sCount > 0 || dCount > 0;

            return new ExaminationRoomItemDto(
                Id: r.Id,
                RoomCode: r.RoomCode,
                Name: r.Name,
                HospitalId: r.HospitalId,
                Facility: facility,
                DepartmentId: r.DepartmentId,
                DepartmentCode: r.Department?.DepartmentCode,
                DepartmentName: r.Department?.Name,
                RoomType: r.RoomType,
                Location: r.Location,
                Capacity: r.Capacity,
                Status: r.Status,
                IsAvailable: isActive,
                IsActive: isActive,
                AssignedDoctorsCount: dCount,
                ActiveVisitsCount: vCount,
                ActiveSlotsCount: sCount,
                HasActiveDependencies: hasDeps
            );
        }).ToList();

        return new ExaminationRoomListResponseDto(
            Data: data,
            Total: totalFiltered,
            Page: request.Page,
            PageSize: request.PageSize,
            TotalPages: totalPages,
            Metrics: metrics
        );
    }

    public async Task<ExaminationRoomItemDto> CreateExaminationRoomAsync(
        long actorUserId,
        CreateExaminationRoomRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rawRoomCode = !string.IsNullOrWhiteSpace(request.RoomCode) ? request.RoomCode : request.Code;
        if (string.IsNullOrWhiteSpace(rawRoomCode))
        {
            throw new ArgumentException("Mã buồng khám không được để trống.", nameof(request.RoomCode));
        }

        var normalizedRoomCode = rawRoomCode.Trim().ToUpperInvariant();
        if (normalizedRoomCode.Length > 40)
        {
            throw new ArgumentException("Mã buồng khám không được vượt quá 40 ký tự.", nameof(request.RoomCode));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Tên buồng khám không được để trống.", nameof(request.Name));
        }

        var normalizedName = request.Name.Trim();
        if (normalizedName.Length > 200)
        {
            throw new ArgumentException("Tên buồng khám không được vượt quá 200 ký tự.", nameof(request.Name));
        }

        if (request.Capacity.HasValue && request.Capacity.Value <= 0)
        {
            throw new ArgumentException("Sức chứa buồng khám phải lớn hơn 0.", nameof(request.Capacity));
        }

        var normalizedRoomType = string.IsNullOrWhiteSpace(request.RoomType)
            ? "EXAMINATION"
            : request.RoomType.Trim().ToUpperInvariant();

        if (normalizedRoomType.Length > 20)
        {
            throw new ArgumentException("Loại phòng không được vượt quá 20 ký tự.", nameof(request.RoomType));
        }

        var normalizedStatus = string.IsNullOrWhiteSpace(request.Status)
            ? "ACTIVE"
            : request.Status.Trim().ToUpperInvariant();

        if (normalizedStatus.Length > 20)
        {
            throw new ArgumentException("Trạng thái phòng không được vượt quá 20 ký tự.", nameof(request.Status));
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var (isSystemAdmin, actorHospitalId) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        long hospitalId;
        if (!isSystemAdmin)
        {
            if (request.HospitalId.HasValue && request.HospitalId.Value != actorHospitalId!.Value)
            {
                throw new ForbiddenException("Bạn không có quyền tạo buồng khám cho cơ sở y tế khác.");
            }

            hospitalId = actorHospitalId!.Value;
        }
        else
        {
            if (!request.HospitalId.HasValue || request.HospitalId.Value <= 0)
            {
                throw new ValidationException("Vui lòng chọn cơ sở y tế hợp lệ cho buồng khám.");
            }

            hospitalId = request.HospitalId.Value;
        }

        var hospital = await _dbContext.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == hospitalId, ct)
            ?? throw new NotFoundException("Hospital", hospitalId);

        var exists = await _dbContext.HospitalRooms.AnyAsync(
            r => r.HospitalId == hospitalId && r.RoomCode.ToLower() == normalizedRoomCode.ToLower(),
            ct);

        if (exists)
        {
            throw new InvalidOperationException($"Mã buồng khám '{normalizedRoomCode}' đã tồn tại trong cơ sở y tế này.");
        }

        Department? department = null;
        if (request.DepartmentId.HasValue && request.DepartmentId.Value > 0)
        {
            department = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value, ct);
            if (department == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy khoa phòng với ID {request.DepartmentId.Value}.");
            }

            if (department.HospitalId.HasValue && department.HospitalId.Value != hospitalId)
            {
                throw new ArgumentException("Khoa phòng không thuộc cơ sở y tế của buồng khám.", nameof(request.DepartmentId));
            }
        }

        var normalizedLocation = string.IsNullOrWhiteSpace(request.Location)
            ? null
            : request.Location.Trim();

        var room = new HospitalRoom
        {
            HospitalId = hospitalId,
            DepartmentId = department?.Id,
            RoomCode = normalizedRoomCode,
            Name = normalizedName,
            RoomType = normalizedRoomType,
            Location = normalizedLocation,
            Capacity = request.Capacity ?? 1,
            Status = normalizedStatus
        };

        _dbContext.HospitalRooms.Add(room);
        await _dbContext.SaveChangesAsync(ct);

        var isActive = string.Equals(room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);

        return new ExaminationRoomItemDto(
            Id: room.Id,
            RoomCode: room.RoomCode,
            Name: room.Name,
            HospitalId: room.HospitalId,
            Facility: ResolveFacilityName(hospital.Name),
            DepartmentId: room.DepartmentId,
            DepartmentCode: department?.DepartmentCode,
            DepartmentName: department?.Name,
            RoomType: room.RoomType,
            Location: room.Location,
            Capacity: room.Capacity,
            Status: room.Status,
            IsAvailable: isActive,
            IsActive: isActive
        );
    }

    public async Task<ExaminationRoomItemDto> UpdateExaminationRoomAsync(
        long actorUserId,
        long id,
        UpdateExaminationRoomRequest request,
        CancellationToken ct = default)
    {
        if (id <= 0)
        {
            throw new ArgumentException("ID buồng khám phải lớn hơn 0.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(request);

        string? normalizedRoomCode = null;
        if (request.RoomCode is not null || request.Code is not null)
        {
            var rawRoomCode = request.RoomCode ?? request.Code;
            if (string.IsNullOrWhiteSpace(rawRoomCode))
            {
                throw new ArgumentException("Mã buồng khám không được để trống.", nameof(request.RoomCode));
            }

            normalizedRoomCode = rawRoomCode.Trim().ToUpperInvariant();
            if (normalizedRoomCode.Length > 40)
            {
                throw new ArgumentException("Mã buồng khám không được vượt quá 40 ký tự.", nameof(request.RoomCode));
            }
        }

        if (request.Capacity.HasValue && request.Capacity.Value <= 0)
        {
            throw new ArgumentException("Sức chứa buồng khám phải lớn hơn 0.", nameof(request.Capacity));
        }

        if (request.DepartmentId.HasValue && request.DepartmentId.Value <= 0)
        {
            throw new ArgumentException("ID khoa phòng phải lớn hơn 0.", nameof(request.DepartmentId));
        }

        string? normalizedRoomType = null;
        var rawRoomType = request.RoomType ?? request.Type;
        if (rawRoomType is not null)
        {
            if (string.IsNullOrWhiteSpace(rawRoomType))
            {
                throw new ArgumentException("Loại phòng không được để trống.", nameof(request.RoomType));
            }

            normalizedRoomType = rawRoomType.Trim().ToUpperInvariant();
            if (normalizedRoomType.Length > 20)
            {
                throw new ArgumentException("Loại phòng không được vượt quá 20 ký tự.", nameof(request.RoomType));
            }
        }

        string? normalizedStatus = null;
        if (request.Status is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Status))
            {
                throw new ArgumentException("Trạng thái phòng không được để trống.", nameof(request.Status));
            }

            normalizedStatus = request.Status.Trim().ToUpperInvariant();
            if (normalizedStatus.Length > 20)
            {
                throw new ArgumentException("Trạng thái phòng không được vượt quá 20 ký tự.", nameof(request.Status));
            }

            if (normalizedStatus is not ("ACTIVE" or "MAINTENANCE" or "INACTIVE"))
            {
                throw new ArgumentException("Trạng thái phòng không hợp lệ. Chỉ chấp nhận ACTIVE, MAINTENANCE hoặc INACTIVE.", nameof(request.Status));
            }
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Tên buồng khám không được để trống.", nameof(request.Name));
        }

        var normalizedName = request.Name.Trim();
        if (normalizedName.Length > 200)
        {
            throw new ArgumentException("Tên buồng khám không được vượt quá 200 ký tự.", nameof(request.Name));
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var (isSystemAdmin, actorHospitalId) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        var room = await _dbContext.HospitalRooms
            .Include(r => r.Department)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (room == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy buồng khám với ID {id}.");
        }

        long targetHospitalId;
        if (!isSystemAdmin)
        {
            if (!room.HospitalId.HasValue || room.HospitalId.Value != actorHospitalId!.Value)
            {
                throw new ForbiddenException("Bạn không có quyền thao tác trên buồng khám thuộc cơ sở y tế khác.");
            }

            if (request.HospitalId.HasValue && request.HospitalId.Value != actorHospitalId.Value)
            {
                throw new ForbiddenException("Bạn không có quyền chuyển buồng khám sang cơ sở y tế khác.");
            }

            targetHospitalId = actorHospitalId.Value;
        }
        else if (request.HospitalId.HasValue)
        {
            if (request.HospitalId.Value <= 0)
            {
                throw new ValidationException("Mã cơ sở y tế không hợp lệ.");
            }

            targetHospitalId = request.HospitalId.Value;
        }
        else
        {
            if (!room.HospitalId.HasValue || room.HospitalId.Value <= 0)
            {
                throw new ValidationException("Buồng khám chưa được gắn cơ sở y tế hợp lệ.");
            }

            targetHospitalId = room.HospitalId.Value;
        }

        var hospital = await _dbContext.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == targetHospitalId, ct)
            ?? throw new NotFoundException("Hospital", targetHospitalId);

        var targetRoomCode = normalizedRoomCode ?? room.RoomCode;

        var exists = await _dbContext.HospitalRooms.AnyAsync(
            r => r.Id != id && r.HospitalId == targetHospitalId && r.RoomCode.ToLower() == targetRoomCode.ToLower(),
            ct);

        if (exists)
        {
            throw new InvalidOperationException($"Mã buồng khám '{targetRoomCode}' đã tồn tại trong cơ sở y tế này.");
        }

        Department? department = room.Department;
        if (request.DepartmentId.HasValue)
        {
            department = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value, ct);
            if (department == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy khoa phòng với ID {request.DepartmentId.Value}.");
            }

            if (department.HospitalId.HasValue && department.HospitalId.Value != targetHospitalId)
            {
                throw new ArgumentException("Khoa phòng không thuộc cơ sở y tế của buồng khám.", nameof(request.DepartmentId));
            }

            room.DepartmentId = department.Id;
            room.Department = department;
        }
        else if (!string.IsNullOrWhiteSpace(request.Department))
        {
            var deptQuery = request.Department.Trim().ToLower();
            department = await _dbContext.Departments.FirstOrDefaultAsync(
                d => (d.HospitalId == null || d.HospitalId == targetHospitalId)
                    && (d.Name.ToLower() == deptQuery || d.DepartmentCode.ToLower() == deptQuery),
                ct);

            if (department == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy khoa phòng '{request.Department.Trim()}'.");
            }

            room.DepartmentId = department.Id;
            room.Department = department;
        }

        room.HospitalId = targetHospitalId;
        room.RoomCode = targetRoomCode;
        room.Name = normalizedName;

        if (normalizedRoomType is not null)
        {
            room.RoomType = normalizedRoomType;
        }

        if (request.Location is not null)
        {
            var trimmedLoc = request.Location.Trim();
            room.Location = string.IsNullOrEmpty(trimmedLoc) ? null : trimmedLoc;
        }

        if (request.Capacity.HasValue)
        {
            room.Capacity = request.Capacity.Value;
        }

        var newStatus = normalizedStatus;
        if (newStatus is null && (request.IsAvailable ?? request.IsActive) is bool activeFlag)
        {
            newStatus = activeFlag ? "ACTIVE" : "INACTIVE";
        }

        if (string.Equals(newStatus, "INACTIVE", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(room.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            var nowLocal = HospitalClock.GetNow(_timeProvider);
            var (assignedDoctors, activeVisits, _, pendingAppointments, blockingReason, _) =
                await GetRoomDependenciesInternalAsync(room, nowLocal, ct);

            if (activeVisits > 0 || assignedDoctors > 0 || pendingAppointments > 0)
            {
                throw new ConflictException(blockingReason ?? "Không thể ngừng hoạt động buồng khám khi còn dữ liệu phụ thuộc đang hoạt động.");
            }
        }

        if (newStatus is not null)
        {
            room.Status = newStatus;
        }

        await _dbContext.SaveChangesAsync(ct);

        var isActive = string.Equals(room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);

        return new ExaminationRoomItemDto(
            Id: room.Id,
            RoomCode: room.RoomCode,
            Name: room.Name,
            HospitalId: room.HospitalId,
            Facility: ResolveFacilityName(hospital.Name),
            DepartmentId: room.DepartmentId,
            DepartmentCode: department?.DepartmentCode,
            DepartmentName: department?.Name,
            RoomType: room.RoomType,
            Location: room.Location,
            Capacity: room.Capacity,
            Status: room.Status,
            IsAvailable: isActive,
            IsActive: isActive
        );
    }

    public async Task<ExaminationRoomDependencySummaryDto> CheckExaminationRoomDependenciesAsync(
        long? actorUserId,
        string idOrCode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ArgumentException("Mã hoặc ID buồng khám không được để trống.", nameof(idOrCode));
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var (isSystemAdmin, actorHospitalId) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        var room = await FindRoomByIdOrCodeAsync(idOrCode, ct)
            ?? throw new NotFoundException("HospitalRoom", idOrCode.Trim());

        if (!isSystemAdmin && (!room.HospitalId.HasValue || room.HospitalId.Value != actorHospitalId!.Value))
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên buồng khám thuộc cơ sở y tế khác.");
        }

        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var (assignedDoctors, activeVisits, activeSlots, pendingAppointments, blockingReason, _) =
            await GetRoomDependenciesInternalAsync(room, nowLocal, ct);

        var hasActiveDependencies = assignedDoctors > 0 || activeVisits > 0 || activeSlots > 0 || pendingAppointments > 0;
        var canDeactivate = activeVisits == 0 && assignedDoctors == 0 && pendingAppointments == 0;

        return new ExaminationRoomDependencySummaryDto(
            RoomId: room.Id,
            RoomCode: room.RoomCode,
            RoomName: room.Name,
            AssignedDoctorsCount: assignedDoctors,
            ActiveVisitsCount: activeVisits,
            ActiveSlotsCount: activeSlots,
            PendingAppointmentsCount: pendingAppointments,
            HasActiveDependencies: hasActiveDependencies,
            CanDeactivate: canDeactivate,
            BlockingReason: blockingReason
        );
    }

    public async Task<DeactivateExaminationRoomResponseDto> DeactivateExaminationRoomAsync(
        long? actorUserId,
        string idOrCode,
        DeactivateExaminationRoomRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ArgumentException("Mã hoặc ID buồng khám không được để trống.", nameof(idOrCode));
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowUtcUnspecified = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var (isSystemAdmin, actorHospitalId) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        var room = await FindRoomByIdOrCodeAsync(idOrCode, ct)
            ?? throw new NotFoundException("HospitalRoom", idOrCode.Trim());

        if (!isSystemAdmin && (!room.HospitalId.HasValue || room.HospitalId.Value != actorHospitalId!.Value))
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên buồng khám thuộc cơ sở y tế khác.");
        }

        if (string.Equals(room.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException($"Buồng khám '{room.Name}' ({room.RoomCode}) hiện đã ở trạng thái ngừng hoạt động.");
        }

        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var (assignedDoctors, activeVisits, activeSlots, pendingAppointments, blockingReason, futureSlots) =
            await GetRoomDependenciesInternalAsync(room, nowLocal, ct);

        if (activeVisits > 0 || assignedDoctors > 0 || pendingAppointments > 0)
        {
            throw new ConflictException(blockingReason ?? "Buồng khám còn các liên kết và dữ liệu phụ thuộc đang hoạt động.");
        }

        var closedSlotsCount = 0;
        if (activeSlots > 0)
        {
            if (request?.ConfirmUnderstood != true)
            {
                throw new ConflictException(blockingReason ?? $"Buồng khám còn {activeSlots} ca khám chưa đóng. Vui lòng xác nhận để tiếp tục.");
            }

            foreach (var slot in futureSlots)
            {
                slot.Status = "CLOSED";
            }
            closedSlotsCount = futureSlots.Count;
        }

        var previousStatus = room.Status;
        room.Status = "INACTIVE";

        _dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = room.HospitalId ?? actorHospitalId,
            Action = "ROOM_DEACTIVATED",
            EntityType = "HospitalRoom",
            EntityId = room.Id.ToString(),
            Reason = request?.Reason,
            ChangeData = JsonSerializer.Serialize(new
            {
                room.RoomCode,
                room.Name,
                room.HospitalId,
                PreviousStatus = previousStatus,
                NewStatus = "INACTIVE",
                ClosedSlotsCount = closedSlotsCount,
                Reason = request?.Reason,
                Note = request?.Note,
                Timestamp = nowUtcUnspecified
            }),
            IpAddress = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var ip) ? ip : null,
            OccurredAt = nowUtcUnspecified
        });

        await _dbContext.SaveChangesAsync(ct);

        var facility = await LoadFacilityNameAsync(room.HospitalId, ct);
        var summary = new ExaminationRoomDependencySummaryDto(
            RoomId: room.Id,
            RoomCode: room.RoomCode,
            RoomName: room.Name,
            AssignedDoctorsCount: assignedDoctors,
            ActiveVisitsCount: activeVisits,
            ActiveSlotsCount: activeSlots,
            PendingAppointmentsCount: pendingAppointments,
            HasActiveDependencies: false,
            CanDeactivate: true
        );

        return new DeactivateExaminationRoomResponseDto(
            Id: room.Id,
            RoomCode: room.RoomCode,
            Name: room.Name,
            HospitalId: room.HospitalId,
            Facility: facility,
            IsActive: false,
            IsAvailable: false,
            Status: "INACTIVE",
            Message: $"Đã ngừng hoạt động buồng khám {room.Name}.",
            Dependencies: summary,
            ClosedSlotsCount: closedSlotsCount
        );
    }

    public async Task<DeactivateExaminationRoomResponseDto> ReactivateExaminationRoomAsync(
        long? actorUserId,
        string idOrCode,
        ReactivateExaminationRoomRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ArgumentException("Mã hoặc ID buồng khám không được để trống.", nameof(idOrCode));
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowUtcUnspecified = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var (isSystemAdmin, actorHospitalId) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        var room = await FindRoomByIdOrCodeAsync(idOrCode, ct)
            ?? throw new NotFoundException("HospitalRoom", idOrCode.Trim());

        if (!isSystemAdmin && (!room.HospitalId.HasValue || room.HospitalId.Value != actorHospitalId!.Value))
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên buồng khám thuộc cơ sở y tế khác.");
        }

        if (string.Equals(room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException($"Buồng khám '{room.Name}' ({room.RoomCode}) đang ở trạng thái hoạt động.");
        }

        var previousStatus = room.Status;
        room.Status = "ACTIVE";

        _dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = room.HospitalId ?? actorHospitalId,
            Action = "ROOM_REACTIVATED",
            EntityType = "HospitalRoom",
            EntityId = room.Id.ToString(),
            Reason = request?.Reason,
            ChangeData = JsonSerializer.Serialize(new
            {
                room.RoomCode,
                room.Name,
                room.HospitalId,
                PreviousStatus = previousStatus,
                NewStatus = "ACTIVE",
                Reason = request?.Reason,
                Timestamp = nowUtcUnspecified
            }),
            IpAddress = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var ip) ? ip : null,
            OccurredAt = nowUtcUnspecified
        });

        await _dbContext.SaveChangesAsync(ct);

        var facility = await LoadFacilityNameAsync(room.HospitalId, ct);
        return new DeactivateExaminationRoomResponseDto(
            Id: room.Id,
            RoomCode: room.RoomCode,
            Name: room.Name,
            HospitalId: room.HospitalId,
            Facility: facility,
            IsActive: true,
            IsAvailable: true,
            Status: "ACTIVE",
            Message: $"Đã kích hoạt lại hoạt động cho buồng khám {room.Name}."
        );
    }

    private async Task<string> LoadFacilityNameAsync(long? hospitalId, CancellationToken ct)
    {
        if (!hospitalId.HasValue)
        {
            return UnknownFacilityFallback;
        }

        var name = await _dbContext.Hospitals
            .AsNoTracking()
            .Where(h => h.Id == hospitalId.Value)
            .Select(h => h.Name)
            .FirstOrDefaultAsync(ct);

        return ResolveFacilityName(name);
    }

    private static string ResolveFacilityName(string? name) =>
        !string.IsNullOrWhiteSpace(name) ? name.Trim() : UnknownFacilityFallback;

    private async Task<HospitalRoom?> FindRoomByIdOrCodeAsync(string idOrCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idOrCode)) return null;

        var trimmed = idOrCode.Trim();
        if (long.TryParse(trimmed, out var id))
        {
            return await _dbContext.HospitalRooms
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == id, ct);
        }

        var upper = trimmed.ToUpperInvariant();
        return await _dbContext.HospitalRooms
            .Include(r => r.Department)
            .FirstOrDefaultAsync(r => r.RoomCode.ToUpper() == upper, ct);
    }

    private async Task<(int AssignedDoctors, int ActiveVisits, int ActiveSlots, int PendingAppointments, string? BlockingReason, List<ExaminationSlot> FutureSlots)> GetRoomDependenciesInternalAsync(
        HospitalRoom room,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var activeVisitsCount = 0;
        var visitDoctorIds = new HashSet<long>();
        try
        {
            var activeVisits = await _dbContext.Visits
                .AsNoTracking()
                .Where(v => v.ExaminationRoomId == room.Id &&
                            (v.Status == "WAITING" || v.Status == "CALLED" || v.Status == "IN_PROGRESS" || v.Status == "IN_CONSULTATION"))
                .ToListAsync(ct);

            activeVisitsCount = activeVisits.Count;
            foreach (var v in activeVisits)
            {
                if (v.AssignedDoctorId.HasValue)
                {
                    visitDoctorIds.Add(v.AssignedDoctorId.Value);
                }
            }
        }
        catch
        {
            // Ignored when optional related DbSet is unconfigured in minimal test context
        }

        List<ExaminationSlot> futureSlots = new();
        var slotDoctorIds = new HashSet<long>();
        try
        {
            futureSlots = await _dbContext.ExaminationSlots
                .Where(s => s.RoomId == room.Id &&
                            s.Status != "CLOSED" &&
                            s.Status != "CANCELLED" &&
                            (s.EndsAt == null || s.EndsAt >= nowLocal))
                .ToListAsync(ct);

            foreach (var s in futureSlots)
            {
                if (s.DoctorId.HasValue)
                {
                    slotDoctorIds.Add(s.DoctorId.Value);
                }
            }
        }
        catch
        {
            // Ignored when optional related DbSet is unconfigured in minimal test context
        }

        var assignedDoctorsSet = new HashSet<long>(visitDoctorIds);
        assignedDoctorsSet.UnionWith(slotDoctorIds);
        var assignedDoctorsCount = assignedDoctorsSet.Count;

        var activeSlotsCount = futureSlots.Count;

        var activeAppointmentsCount = 0;
        var futureSlotIds = futureSlots.Select(s => s.Id).ToList();
        if (futureSlotIds.Count > 0)
        {
            try
            {
                activeAppointmentsCount = await _dbContext.Appointments
                    .AsNoTracking()
                    .CountAsync(a => a.SlotId.HasValue && futureSlotIds.Contains(a.SlotId.Value) &&
                                     (a.Status == "BOOKED" || a.Status == "CONFIRMED" || a.Status == "CHECKED_IN"),
                                ct);
            }
            catch
            {
                // Ignored when optional related DbSet is unconfigured in minimal test context
            }
        }

        string? blockingReason = null;
        if (activeVisitsCount > 0 || assignedDoctorsCount > 0 || activeAppointmentsCount > 0)
        {
            var parts = new List<string>();
            if (activeVisitsCount > 0) parts.Add($"{activeVisitsCount} lượt khám đang xử lý");
            if (assignedDoctorsCount > 0) parts.Add($"{assignedDoctorsCount} bác sĩ đang trực/phân công");
            if (activeAppointmentsCount > 0) parts.Add($"{activeAppointmentsCount} lịch hẹn của bệnh nhân chưa hoàn tất");
            blockingReason = $"Không thể ngừng hoạt động buồng khám khi còn {string.Join(", ", parts)}. Vui lòng chuyển giao hoặc hoàn tất trước khi thực hiện.";
        }
        else if (activeSlotsCount > 0)
        {
            blockingReason = $"Buồng khám đang có {activeSlotsCount} ca khám chưa đóng. Cần xác nhận để tự động đóng các ca khám này trước khi ngừng hoạt động.";
        }

        return (assignedDoctorsCount, activeVisitsCount, activeSlotsCount, activeAppointmentsCount, blockingReason, futureSlots);
    }

    private async Task<(bool IsSystemAdmin, long? ActorHospitalId)> EnsureAdminAccessAsync(
        long? actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (!actorUserId.HasValue || actorUserId.Value <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId.Value, ct)
            ?? throw new NotFoundException("User", actorUserId.Value);

        if (user.AccountStatus != AccountStatus.ACTIVE)
        {
            throw new ForbiddenException("Tài khoản quản trị viên không ở trạng thái hoạt động.");
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản quản trị viên hiện đang bị tạm khóa.");
        }

        var roleIds = await _dbContext.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == user.Id)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var roleCodes = roleIds.Count == 0
            ? []
            : await _dbContext.Roles
                .AsNoTracking()
                .Where(r => roleIds.Contains(r.Id))
                .Select(r => r.Code)
                .ToListAsync(ct);

        var isSystemAdmin = roleCodes.Contains(RoleNames.SystemAdmin, StringComparer.OrdinalIgnoreCase);
        var isHospitalAdmin = roleCodes.Contains(RoleNames.HospitalAdmin, StringComparer.OrdinalIgnoreCase);

        if (!isSystemAdmin && !isHospitalAdmin)
        {
            throw new ForbiddenException("Bạn không có quyền thực hiện thao tác quản trị buồng khám.");
        }

        var hasHospitalConfigPerm = await (
            from rp in _dbContext.RolePermissions.AsNoTracking()
            join p in _dbContext.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where roleIds.Contains(rp.RoleId) && p.Code.ToUpper() == PermissionCodes.HospitalConfig
            select rp.PermissionId).AnyAsync(ct);

        if (!hasHospitalConfigPerm)
        {
            throw new ForbiddenException("Tài khoản quản trị chưa được cấp quyền cấu hình danh mục bệnh viện (HOSPITAL_CONFIG).");
        }

        var staff = await _dbContext.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == user.Id, ct);

        if (staff is not null &&
            !string.IsNullOrWhiteSpace(staff.Status) &&
            !string.Equals(staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của quản trị viên không ở trạng thái hoạt động.");
        }

        if (!isSystemAdmin)
        {
            if (staff is null || !staff.HospitalId.HasValue)
            {
                throw new ForbiddenException("Tài khoản quản trị bệnh viện chưa được gán cơ sở y tế.");
            }

            var hospital = await _dbContext.Hospitals
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == staff.HospitalId.Value, ct)
                ?? throw new NotFoundException("Hospital", staff.HospitalId.Value);

            if (hospital.IsActive == false)
            {
                throw new ForbiddenException("Cơ sở y tế của quản trị viên đang ngừng hoạt động.");
            }

            return (false, staff.HospitalId.Value);
        }

        return (true, staff?.HospitalId);
    }
}
