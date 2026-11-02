using System.Globalization;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Services;

public sealed class ExaminationServiceAdminService : IExaminationServiceAdminService
{
    private readonly IApplicationDbContext _db;

    public ExaminationServiceAdminService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ExaminationServiceAdminListResponseDto> GetExaminationServiceListAsync(
        GetExaminationServiceListAdminRequest request,
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

        var baseQuery = _db.Services.AsNoTracking();

        // 1. Calculate overall metrics across all services in database
        var allServices = await baseQuery.Select(s => new
        {
            s.DepartmentId,
            s.IsActive,
            s.AllowsOnlineBooking
        }).ToListAsync(ct);

        var totalAll = allServices.Count;
        var activeCount = allServices.Count(s => s.IsActive ?? true);
        var suspendedCount = totalAll - activeCount;
        var onlineBookingCount = allServices.Count(s => s.AllowsOnlineBooking ?? false);
        var departmentsCount = allServices.Select(s => s.DepartmentId).Where(d => d.HasValue).Distinct().Count();

        var metrics = new ExaminationServiceAdminMetricsDto(
            Total: totalAll,
            Active: activeCount,
            Suspended: suspendedCount,
            OnlineBookingCount: onlineBookingCount,
            DepartmentsCount: departmentsCount
        );

        // 2. Build filtered query with related entities
        var filteredQuery = baseQuery
            .Include(s => s.Department)
            .Include(s => s.Specialty)
            .Include(s => s.ServicePrices)
            .AsQueryable();

        // Keyword filter: ServiceCode, Name, Description
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim().ToLower();
            filteredQuery = filteredQuery.Where(s =>
                s.ServiceCode.ToLower().Contains(kw) ||
                s.Name.ToLower().Contains(kw) ||
                (s.Description != null && s.Description.ToLower().Contains(kw)));
        }

        // Department filter
        if (request.DepartmentId.HasValue)
        {
            filteredQuery = filteredQuery.Where(s => s.DepartmentId == request.DepartmentId.Value);
        }

        // Specialty filter
        if (request.SpecialtyId.HasValue)
        {
            filteredQuery = filteredQuery.Where(s => s.SpecialtyId == request.SpecialtyId.Value);
        }

        // Hospital filter
        if (request.HospitalId.HasValue)
        {
            filteredQuery = filteredQuery.Where(s => s.HospitalId == request.HospitalId.Value);
        }

        // ServiceType / Category filter
        var typeOrCategoryFilter = !string.IsNullOrWhiteSpace(request.Category)
            ? request.Category
            : request.ServiceType;

        if (!string.IsNullOrWhiteSpace(typeOrCategoryFilter))
        {
            var filter = typeOrCategoryFilter.Trim().ToLower();
            if (filter is "khám lâm sàng" or "examination")
            {
                filteredQuery = filteredQuery.Where(s => s.ServiceType.ToLower() == "examination");
            }
            else if (filter is "hội chẩn lâm sàng" or "consultation")
            {
                filteredQuery = filteredQuery.Where(s => s.ServiceType.ToLower() == "consultation");
            }
            else if (filter is "cận lâm sàng kỹ thuật" or "diagnostic")
            {
                filteredQuery = filteredQuery.Where(s => s.ServiceType.ToLower() == "diagnostic");
            }
            else
            {
                filteredQuery = filteredQuery.Where(s => s.ServiceType.ToLower() == filter);
            }
        }

        // BookingChannel / AllowsOnlineBooking filter
        if (request.AllowsOnlineBooking.HasValue)
        {
            filteredQuery = filteredQuery.Where(s => s.AllowsOnlineBooking == request.AllowsOnlineBooking.Value);
        }
        else if (!string.IsNullOrWhiteSpace(request.BookingChannel))
        {
            var ch = request.BookingChannel.Trim().ToLower();
            if (ch is "online_allowed" or "online")
            {
                filteredQuery = filteredQuery.Where(s => s.AllowsOnlineBooking == true);
            }
            else if (ch is "counter_only" or "counter")
            {
                filteredQuery = filteredQuery.Where(s => s.AllowsOnlineBooking == false);
            }
        }

        // Status / IsActive filter
        if (request.IsActive.HasValue)
        {
            filteredQuery = filteredQuery.Where(s => s.IsActive == request.IsActive.Value);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var st = request.Status.Trim().ToLower();
            if (st == "active")
            {
                filteredQuery = filteredQuery.Where(s => s.IsActive == true);
            }
            else if (st is "suspended" or "inactive")
            {
                filteredQuery = filteredQuery.Where(s => s.IsActive == false);
            }
        }

        // 3. Count & Pagination
        var totalFiltered = await filteredQuery.CountAsync(ct);
        var totalPages = totalFiltered == 0 ? 1 : (int)Math.Ceiling((double)totalFiltered / request.PageSize);

        var entities = await filteredQuery
            .OrderBy(s => s.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        // 4. Map to DTOs
        var items = entities.Select(s =>
        {
            var isActive = s.IsActive ?? true;
            var status = isActive ? "ACTIVE" : "SUSPENDED";
            var allowsOnline = s.AllowsOnlineBooking ?? false;
            var bookingChannel = allowsOnline ? "ONLINE_ALLOWED" : "COUNTER_ONLY";
            var category = ResolveCategory(s.ServiceType);
            var facility = s.HospitalId.HasValue && s.HospitalId.Value > 1
                ? $"MedFlow Cơ sở {s.HospitalId.Value}"
                : "MedFlow Cơ sở 1";

            var activePrice = s.ServicePrices
                .Where(p => (!p.EffectiveFrom.HasValue || p.EffectiveFrom.Value <= now)
                         && (!p.EffectiveTo.HasValue || p.EffectiveTo.Value > now))
                .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
                .ThenByDescending(p => p.Id)
                .Select(p => p.PriceAmount)
                .FirstOrDefault()
                ?? s.ServicePrices.OrderByDescending(p => p.Id).Select(p => p.PriceAmount).FirstOrDefault()
                ?? 0m;

            var priceFormatted = activePrice.ToString("N0", new CultureInfo("vi-VN")) + " đ";

            return new ExaminationServiceAdminItemDto(
                Id: s.Id,
                ServiceCode: s.ServiceCode,
                Name: s.Name,
                Description: s.Description,
                ServiceType: s.ServiceType,
                Category: category,
                HospitalId: s.HospitalId,
                Facility: facility,
                DepartmentId: s.DepartmentId,
                DepartmentCode: s.Department?.DepartmentCode,
                DepartmentName: s.Department?.Name,
                SpecialtyId: s.SpecialtyId,
                SpecialtyCode: s.Specialty?.SpecialtyCode,
                SpecialtyName: s.Specialty?.Name,
                Price: activePrice,
                PriceFormatted: priceFormatted,
                AllowsOnlineBooking: allowsOnline,
                BookingChannel: bookingChannel,
                RequiresResultVerification: s.RequiresResultVerification ?? false,
                IsActive: isActive,
                Status: status
            );
        }).ToList();

        return new ExaminationServiceAdminListResponseDto(
            Data: items,
            Total: totalFiltered,
            Page: request.Page,
            PageSize: request.PageSize,
            TotalPages: totalPages,
            Metrics: metrics
        );
    }

    private static string ResolveCategory(string? serviceType)
    {
        if (string.Equals(serviceType, "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
            return "Cận lâm sàng kỹ thuật";
        if (string.Equals(serviceType, "CONSULTATION", StringComparison.OrdinalIgnoreCase))
            return "Hội chẩn Lâm sàng";
        return "Khám Lâm sàng";
    }
}
