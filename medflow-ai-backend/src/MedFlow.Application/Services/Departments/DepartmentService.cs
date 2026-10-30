using MedFlow.Application.DTOs.Departments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
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
}

