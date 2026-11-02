using MedFlow.Application.DTOs.Departments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Departments;

public sealed class DepartmentService : IDepartmentService
{
    private readonly IApplicationDbContext _dbContext;

    public DepartmentService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DepartmentListResponseDto> GetDepartmentListAsync(
        GetDepartmentListRequest request,
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

        var baseQuery = _dbContext.Departments.AsNoTracking();

        // Calculate overall metrics
        var allDepartments = await baseQuery.ToListAsync(ct);
        var totalAll = allDepartments.Count;
        var activeCount = allDepartments.Count(d => d.IsActive == true);
        var inactiveCount = allDepartments.Count(d => d.IsActive == false);
        var facilityCount = allDepartments.Select(d => d.HospitalId).Where(h => h.HasValue).Distinct().Count();
        if (facilityCount == 0)
        {
            facilityCount = 1;
        }

        var metrics = new DepartmentMetricsDto(totalAll, activeCount, inactiveCount, facilityCount);

        // Apply filters
        var filteredQuery = baseQuery;

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim().ToLower();
            filteredQuery = filteredQuery.Where(d =>
                d.DepartmentCode.ToLower().Contains(kw) ||
                d.Name.ToLower().Contains(kw));
        }

        if (request.IsActive.HasValue)
        {
            filteredQuery = filteredQuery.Where(d => d.IsActive == request.IsActive.Value);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (string.Equals(request.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(d => d.IsActive == true);
            }
            else if (string.Equals(request.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(d => d.IsActive == false);
            }
        }

        if (request.HospitalId.HasValue)
        {
            filteredQuery = filteredQuery.Where(d => d.HospitalId == request.HospitalId.Value);
        }

        var totalFiltered = await filteredQuery.CountAsync(ct);
        var totalPages = totalFiltered == 0 ? 1 : (int)Math.Ceiling((double)totalFiltered / request.PageSize);

        var entities = await filteredQuery
            .OrderBy(d => d.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var data = entities.Select(d =>
        {
            var isActive = d.IsActive ?? true;
            var facility = d.HospitalId.HasValue && d.HospitalId.Value > 1
                ? $"MedFlow Cơ sở {d.HospitalId.Value}"
                : "MedFlow Cơ sở 1";

            return new DepartmentItemDto(
                Id: d.Id,
                Code: d.DepartmentCode,
                Name: d.Name,
                HospitalId: d.HospitalId,
                Facility: facility,
                IsActive: isActive,
                Status: isActive ? "ACTIVE" : "INACTIVE"
            );
        }).ToList();

        return new DepartmentListResponseDto(
            Data: data,
            Total: totalFiltered,
            Page: request.Page,
            PageSize: request.PageSize,
            TotalPages: totalPages,
            Metrics: metrics
        );
    }

    public async Task<DepartmentItemDto> CreateDepartmentAsync(
        CreateDepartmentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new ArgumentException("Mã khoa phòng không được để trống.", nameof(request.Code));
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        if (normalizedCode.Length > 40)
        {
            throw new ArgumentException("Mã khoa phòng không được vượt quá 40 ký tự.", nameof(request.Code));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Tên khoa phòng không được để trống.", nameof(request.Name));
        }

        var normalizedName = request.Name.Trim();
        if (normalizedName.Length > 200)
        {
            throw new ArgumentException("Tên khoa phòng không được vượt quá 200 ký tự.", nameof(request.Name));
        }

        var hospitalId = request.HospitalId ?? 1;

        var exists = await _dbContext.Departments.AnyAsync(
            d => d.HospitalId == hospitalId && d.DepartmentCode.ToLower() == normalizedCode.ToLower(),
            ct);

        if (exists)
        {
            throw new InvalidOperationException($"Mã khoa phòng '{normalizedCode}' đã tồn tại trong cơ sở này.");
        }

        var isActive = request.IsActive ?? true;

        var department = new Department
        {
            HospitalId = hospitalId,
            DepartmentCode = normalizedCode,
            Name = normalizedName,
            IsActive = isActive
        };

        _dbContext.Departments.Add(department);
        await _dbContext.SaveChangesAsync(ct);

        var facility = hospitalId > 1
            ? $"MedFlow Cơ sở {hospitalId}"
            : "MedFlow Cơ sở 1";

        return new DepartmentItemDto(
            Id: department.Id,
            Code: department.DepartmentCode,
            Name: department.Name,
            HospitalId: department.HospitalId,
            Facility: facility,
            IsActive: isActive,
            Status: isActive ? "ACTIVE" : "INACTIVE"
        );
    }

    public async Task<DepartmentItemDto> UpdateDepartmentAsync(
        long id,
        UpdateDepartmentRequest request,
        CancellationToken ct = default)
    {
        if (id <= 0)
        {
            throw new ArgumentException("ID khoa phòng phải lớn hơn 0.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new ArgumentException("Mã khoa phòng không được để trống.", nameof(request.Code));
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        if (normalizedCode.Length > 40)
        {
            throw new ArgumentException("Mã khoa phòng không được vượt quá 40 ký tự.", nameof(request.Code));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Tên khoa phòng không được để trống.", nameof(request.Name));
        }

        var normalizedName = request.Name.Trim();
        if (normalizedName.Length > 200)
        {
            throw new ArgumentException("Tên khoa phòng không được vượt quá 200 ký tự.", nameof(request.Name));
        }

        var dept = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dept == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy khoa phòng với ID {id}.");
        }

        var targetHospitalId = request.HospitalId.HasValue && request.HospitalId.Value > 0
            ? request.HospitalId.Value
            : (dept.HospitalId ?? 1);

        // Composite uniqueness check: (hospital_id, department_code) excluding current entity
        var isDuplicate = await _dbContext.Departments.AnyAsync(d =>
            d.Id != id &&
            d.HospitalId == targetHospitalId &&
            d.DepartmentCode.ToLower() == normalizedCode.ToLower(),
            ct);

        if (isDuplicate)
        {
            throw new InvalidOperationException($"Mã khoa phòng '{normalizedCode}' đã tồn tại trong cơ sở y tế này.");
        }

        dept.DepartmentCode = normalizedCode;
        dept.Name = normalizedName;
        dept.HospitalId = targetHospitalId;

        if (request.IsActive.HasValue)
        {
            dept.IsActive = request.IsActive.Value;
        }

        await _dbContext.SaveChangesAsync(ct);

        var isActive = dept.IsActive ?? true;
        var facility = dept.HospitalId.HasValue && dept.HospitalId.Value > 1
            ? $"MedFlow Cơ sở {dept.HospitalId.Value}"
            : "MedFlow Cơ sở 1";

        return new DepartmentItemDto(
            Id: dept.Id,
            Code: dept.DepartmentCode,
            Name: dept.Name,
            HospitalId: dept.HospitalId,
            Facility: facility,
            IsActive: isActive,
            Status: isActive ? "ACTIVE" : "INACTIVE"
        );
    }
}
