using MedFlow.Application.DTOs.ExaminationRooms;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.ExaminationRooms;

public sealed class ExaminationRoomService : IExaminationRoomService
{
    private readonly IApplicationDbContext _dbContext;

    public ExaminationRoomService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ExaminationRoomListResponseDto> GetExaminationRoomListAsync(
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

        var baseQuery = _dbContext.HospitalRooms.AsNoTracking();

        // Calculate overall metrics across the system
        var allRooms = await baseQuery.ToListAsync(ct);
        var totalAll = allRooms.Count;
        var activeCount = allRooms.Count(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase));
        var inactiveCount = totalAll - activeCount;
        var departmentsCount = allRooms.Select(r => r.DepartmentId).Where(d => d.HasValue).Distinct().Count();

        var metrics = new ExaminationRoomMetricsDto(totalAll, activeCount, inactiveCount, departmentsCount);

        // Apply filters
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

        if (request.HospitalId.HasValue)
        {
            filteredQuery = filteredQuery.Where(r => r.HospitalId == request.HospitalId.Value);
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

        var data = entities.Select(r =>
        {
            var isActive = string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);
            var facility = r.HospitalId.HasValue && r.HospitalId.Value > 1
                ? $"MedFlow Cơ sở {r.HospitalId.Value}"
                : "MedFlow Cơ sở 1";

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
                IsActive: isActive
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

        var hospitalId = request.HospitalId.HasValue && request.HospitalId.Value > 0
            ? request.HospitalId.Value
            : 1;

        // Verify composite uniqueness (hospital_id, room_code)
        var exists = await _dbContext.HospitalRooms.AnyAsync(
            r => r.HospitalId == hospitalId && r.RoomCode.ToLower() == normalizedRoomCode.ToLower(),
            ct);

        if (exists)
        {
            throw new InvalidOperationException($"Mã buồng khám '{normalizedRoomCode}' đã tồn tại trong cơ sở y tế này.");
        }

        // Verify DepartmentId exists if specified
        Department? department = null;
        if (request.DepartmentId.HasValue && request.DepartmentId.Value > 0)
        {
            department = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value, ct);
            if (department == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy khoa phòng với ID {request.DepartmentId.Value}.");
            }
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

        var normalizedLocation = string.IsNullOrWhiteSpace(request.Location)
            ? null
            : request.Location.Trim();

        var room = new HospitalRoom
        {
            HospitalId = hospitalId,
            DepartmentId = department?.Id ?? (request.DepartmentId.HasValue && request.DepartmentId.Value > 0 ? request.DepartmentId : null),
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
        var facility = hospitalId > 1
            ? $"MedFlow Cơ sở {hospitalId}"
            : "MedFlow Cơ sở 1";

        return new ExaminationRoomItemDto(
            Id: room.Id,
            RoomCode: room.RoomCode,
            Name: room.Name,
            HospitalId: room.HospitalId,
            Facility: facility,
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
}
