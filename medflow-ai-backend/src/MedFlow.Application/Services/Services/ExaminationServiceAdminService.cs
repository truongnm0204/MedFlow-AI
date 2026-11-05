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

    public async Task<ExaminationServiceAdminItemDto> CreateExaminationServiceAsync(
        CreateExaminationServiceRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Validate ServiceCode
        var rawCode = request.ServiceCode ?? request.Code;
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            throw new ArgumentException("Mã dịch vụ khám không được để trống.", nameof(request.ServiceCode));
        }

        var serviceCode = rawCode.Trim().ToUpperInvariant();
        if (serviceCode.Length < 2)
        {
            throw new ArgumentException("Mã dịch vụ phải có ít nhất 2 ký tự.", nameof(request.ServiceCode));
        }

        if (serviceCode.Length > 40)
        {
            throw new ArgumentException("Mã dịch vụ không được vượt quá 40 ký tự.", nameof(request.ServiceCode));
        }

        // 2. Validate Name
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Tên dịch vụ khám không được để trống.", nameof(request.Name));
        }

        var name = request.Name.Trim();
        if (name.Length < 3)
        {
            throw new ArgumentException("Tên dịch vụ phải có ít nhất 3 ký tự.", nameof(request.Name));
        }

        if (name.Length > 200)
        {
            throw new ArgumentException("Tên dịch vụ không được vượt quá 200 ký tự.", nameof(request.Name));
        }

        // 3. Normalize ServiceType / Category
        var rawCategory = request.Category ?? request.ServiceType;
        var serviceType = "EXAMINATION";
        if (!string.IsNullOrWhiteSpace(rawCategory))
        {
            var cat = rawCategory.Trim().ToLowerInvariant();
            if (cat is "khám lâm sàng" or "examination")
            {
                serviceType = "EXAMINATION";
            }
            else if (cat is "hội chẩn lâm sàng" or "consultation")
            {
                serviceType = "CONSULTATION";
            }
            else if (cat is "cận lâm sàng kỹ thuật" or "diagnostic")
            {
                serviceType = "DIAGNOSTIC";
            }
            else
            {
                throw new ArgumentException("Phân loại dịch vụ không hợp lệ. Cho phép: Khám Lâm sàng (EXAMINATION), Hội chẩn Lâm sàng (CONSULTATION), Cận lâm sàng kỹ thuật (DIAGNOSTIC).", nameof(request.Category));
            }
        }

        // 4. Validate Department
        long? departmentId = request.DepartmentId;
        Department? department = null;
        if (departmentId.HasValue)
        {
            department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == departmentId.Value, ct);
            if (department is null)
            {
                throw new KeyNotFoundException($"Không tìm thấy khoa phòng với mã {departmentId.Value}.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.Department))
        {
            var deptQuery = request.Department.Trim().ToLower();
            department = await _db.Departments.FirstOrDefaultAsync(
                d => d.Name.ToLower() == deptQuery || d.DepartmentCode.ToLower() == deptQuery, ct);
            if (department is null)
            {
                throw new KeyNotFoundException($"Không tìm thấy khoa phòng: {request.Department.Trim()}.");
            }
            departmentId = department.Id;
        }

        // 5. Validate Specialty if provided
        Specialty? specialty = null;
        if (request.SpecialtyId.HasValue)
        {
            specialty = await _db.Specialties.FirstOrDefaultAsync(s => s.Id == request.SpecialtyId.Value, ct);
            if (specialty is null)
            {
                throw new KeyNotFoundException($"Không tìm thấy chuyên khoa với mã {request.SpecialtyId.Value}.");
            }
        }

        // 6. HospitalId & Unique check
        var hospitalId = request.HospitalId.GetValueOrDefault(1);
        var isDuplicate = await _db.Services.AnyAsync(
            s => (s.HospitalId ?? 1) == hospitalId && s.ServiceCode.ToLower() == serviceCode.ToLower(), ct);

        if (isDuplicate)
        {
            throw new InvalidOperationException($"Mã dịch vụ '{serviceCode}' đã tồn tại trong cơ sở y tế.");
        }

        // 7. Validate Price if provided
        var price = request.Price ?? request.InitialPrice;
        if (price.HasValue && price.Value < 0)
        {
            throw new ArgumentException("Giá dịch vụ không được là số âm.", nameof(request.Price));
        }

        // 8. Flags & Status
        var allowsOnline = request.AllowsOnlineBooking ?? request.OnlineBooking ?? true;
        var requiresVerification = request.RequiresResultVerification ?? (serviceType == "DIAGNOSTIC");

        bool isActive;
        if (request.IsActive.HasValue)
        {
            isActive = request.IsActive.Value;
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var st = request.Status.Trim();
            isActive = !string.Equals(st, "SUSPENDED", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(st, "INACTIVE", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            isActive = true;
        }

        // 9. Persist Service
        var newService = new Service
        {
            ServiceCode = serviceCode,
            Name = name,
            ServiceType = serviceType,
            DepartmentId = departmentId,
            SpecialtyId = request.SpecialtyId,
            HospitalId = hospitalId,
            Description = request.Description?.Trim(),
            AllowsOnlineBooking = allowsOnline,
            RequiresResultVerification = requiresVerification,
            IsActive = isActive
        };

        _db.Services.Add(newService);
        await _db.SaveChangesAsync(ct);

        // 10. Persist Price if provided
        var effectivePrice = price.GetValueOrDefault(0m);
        if (price.HasValue)
        {
            var nowUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            var servicePrice = new ServicePrice
            {
                ServiceId = newService.Id,
                PriceAmount = price.Value,
                EffectiveFrom = nowUtc,
                EffectiveTo = null
            };
            _db.ServicePrices.Add(servicePrice);
            await _db.SaveChangesAsync(ct);
        }

        // 11. Format & return DTO
        var facility = hospitalId > 1 ? $"MedFlow Cơ sở {hospitalId}" : "MedFlow Cơ sở 1";
        var priceFormatted = effectivePrice.ToString("N0", new CultureInfo("vi-VN")) + " đ";
        var categoryName = ResolveCategory(serviceType);
        var bookingChannel = allowsOnline ? "ONLINE_ALLOWED" : "COUNTER_ONLY";
        var statusStr = isActive ? "ACTIVE" : "SUSPENDED";

        return new ExaminationServiceAdminItemDto(
            Id: newService.Id,
            ServiceCode: newService.ServiceCode,
            Name: newService.Name,
            Description: newService.Description,
            ServiceType: newService.ServiceType,
            Category: categoryName,
            HospitalId: hospitalId,
            Facility: facility,
            DepartmentId: departmentId,
            DepartmentCode: department?.DepartmentCode,
            DepartmentName: department?.Name,
            SpecialtyId: request.SpecialtyId,
            SpecialtyCode: specialty?.SpecialtyCode,
            SpecialtyName: specialty?.Name,
            Price: effectivePrice,
            PriceFormatted: priceFormatted,
            AllowsOnlineBooking: allowsOnline,
            BookingChannel: bookingChannel,
            RequiresResultVerification: requiresVerification,
            IsActive: isActive,
            Status: statusStr
        );
    }

    public async Task<ExaminationServiceAdminItemDto> UpdateExaminationServiceAsync(
        long id,
        UpdateExaminationServiceRequest request,
        CancellationToken ct = default)
    {
        if (id <= 0)
        {
            throw new ArgumentException("ID dịch vụ khám phải lớn hơn 0.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(request);

        // Pre-validate ServiceCode format if provided
        var rawCode = request.ServiceCode ?? request.Code;
        string? newCode = null;
        if (!string.IsNullOrWhiteSpace(rawCode))
        {
            newCode = rawCode.Trim().ToUpperInvariant();
            if (newCode.Length < 2)
            {
                throw new ArgumentException("Mã dịch vụ phải có ít nhất 2 ký tự.", nameof(request.ServiceCode));
            }

            if (newCode.Length > 40)
            {
                throw new ArgumentException("Mã dịch vụ không được vượt quá 40 ký tự.", nameof(request.ServiceCode));
            }
        }

        // Pre-validate Name if provided
        string? newName = null;
        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new ArgumentException("Tên dịch vụ khám không được để trống.", nameof(request.Name));
            }

            newName = request.Name.Trim();
            if (newName.Length < 3)
            {
                throw new ArgumentException("Tên dịch vụ phải có ít nhất 3 ký tự.", nameof(request.Name));
            }

            if (newName.Length > 200)
            {
                throw new ArgumentException("Tên dịch vụ không được vượt quá 200 ký tự.", nameof(request.Name));
            }
        }

        // Pre-validate Category / ServiceType if provided
        var rawCategory = request.Category ?? request.ServiceType;
        string? newServiceType = null;
        if (!string.IsNullOrWhiteSpace(rawCategory))
        {
            var cat = rawCategory.Trim().ToLowerInvariant();
            if (cat is "khám lâm sàng" or "examination")
            {
                newServiceType = "EXAMINATION";
            }
            else if (cat is "hội chẩn lâm sàng" or "consultation")
            {
                newServiceType = "CONSULTATION";
            }
            else if (cat is "cận lâm sàng kỹ thuật" or "diagnostic")
            {
                newServiceType = "DIAGNOSTIC";
            }
            else
            {
                throw new ArgumentException("Phân loại dịch vụ không hợp lệ. Cho phép: Khám Lâm sàng (EXAMINATION), Hội chẩn Lâm sàng (CONSULTATION), Cận lâm sàng kỹ thuật (DIAGNOSTIC).", nameof(request.Category));
            }
        }

        // Pre-validate Price if provided
        if (request.Price.HasValue && request.Price.Value < 0)
        {
            throw new ArgumentException("Giá dịch vụ không được là số âm.", nameof(request.Price));
        }

        var service = await _db.Services
            .Include(s => s.Department)
            .Include(s => s.Specialty)
            .Include(s => s.ServicePrices)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (service is null)
        {
            throw new KeyNotFoundException($"Không tìm thấy dịch vụ khám với ID {id}.");
        }

        // 1. ServiceCode update & duplicate check
        if (newCode is not null)
        {
            var targetHospitalId = request.HospitalId ?? service.HospitalId ?? 1;
            if (!string.Equals(service.ServiceCode, newCode, StringComparison.OrdinalIgnoreCase) ||
                (request.HospitalId.HasValue && request.HospitalId.Value != service.HospitalId))
            {
                var isDuplicate = await _db.Services.AnyAsync(
                    s => s.Id != id && (s.HospitalId ?? 1) == targetHospitalId && s.ServiceCode.ToLower() == newCode.ToLower(), ct);

                if (isDuplicate)
                {
                    throw new InvalidOperationException($"Mã dịch vụ '{newCode}' đã tồn tại trong cơ sở y tế.");
                }
            }

            service.ServiceCode = newCode;
        }

        // 2. Name
        if (newName is not null)
        {
            service.Name = newName;
        }

        // 3. Category / ServiceType
        if (newServiceType is not null)
        {
            service.ServiceType = newServiceType;
        }

        // 4. Department
        if (request.DepartmentId.HasValue)
        {
            var dept = await _db.Departments.FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value, ct);
            if (dept is null)
            {
                throw new KeyNotFoundException($"Không tìm thấy khoa phòng với mã {request.DepartmentId.Value}.");
            }
            service.DepartmentId = dept.Id;
            service.Department = dept;
        }
        else if (!string.IsNullOrWhiteSpace(request.Department))
        {
            var deptQuery = request.Department.Trim().ToLower();
            var dept = await _db.Departments.FirstOrDefaultAsync(
                d => d.Name.ToLower() == deptQuery || d.DepartmentCode.ToLower() == deptQuery, ct);
            if (dept is null)
            {
                throw new KeyNotFoundException($"Không tìm thấy khoa phòng: {request.Department.Trim()}.");
            }
            service.DepartmentId = dept.Id;
            service.Department = dept;
        }

        // 5. Specialty
        if (request.SpecialtyId.HasValue)
        {
            var spec = await _db.Specialties.FirstOrDefaultAsync(s => s.Id == request.SpecialtyId.Value, ct);
            if (spec is null)
            {
                throw new KeyNotFoundException($"Không tìm thấy chuyên khoa với mã {request.SpecialtyId.Value}.");
            }
            service.SpecialtyId = spec.Id;
            service.Specialty = spec;
        }

        // 6. HospitalId
        if (request.HospitalId.HasValue)
        {
            service.HospitalId = request.HospitalId.Value;
        }

        // 7. Description
        if (request.Description is not null)
        {
            service.Description = request.Description.Trim();
        }

        // 8. Online Booking
        var allowsOnline = request.AllowsOnlineBooking ?? request.OnlineBooking;
        if (allowsOnline.HasValue)
        {
            service.AllowsOnlineBooking = allowsOnline.Value;
        }

        // 9. RequiresResultVerification
        if (request.RequiresResultVerification.HasValue)
        {
            service.RequiresResultVerification = request.RequiresResultVerification.Value;
        }
        else if (service.ServiceType == "DIAGNOSTIC" && !service.RequiresResultVerification.HasValue)
        {
            service.RequiresResultVerification = true;
        }

        // 10. Status / IsActive
        if (request.IsActive.HasValue)
        {
            service.IsActive = request.IsActive.Value;
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var st = request.Status.Trim();
            service.IsActive = !string.Equals(st, "SUSPENDED", StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(st, "INACTIVE", StringComparison.OrdinalIgnoreCase);
        }

        // 11. Price update if provided
        if (request.Price.HasValue)
        {
            if (request.Price.Value < 0)
            {
                throw new ArgumentException("Giá dịch vụ không được là số âm.", nameof(request.Price));
            }

            var nowUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            // Close existing active prices
            foreach (var existingPrice in service.ServicePrices.Where(p => !p.EffectiveTo.HasValue || p.EffectiveTo > nowUtc))
            {
                existingPrice.EffectiveTo = nowUtc;
            }

            var newPrice = new ServicePrice
            {
                ServiceId = service.Id,
                PriceAmount = request.Price.Value,
                EffectiveFrom = nowUtc,
                EffectiveTo = null
            };
            service.ServicePrices.Add(newPrice);
            _db.ServicePrices.Add(newPrice);
        }

        await _db.SaveChangesAsync(ct);

        // 12. Resolve effective price and return DTO
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var activePrice = service.ServicePrices
            .Where(p => (!p.EffectiveFrom.HasValue || p.EffectiveFrom.Value <= now)
                     && (!p.EffectiveTo.HasValue || p.EffectiveTo.Value > now))
            .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
            .ThenByDescending(p => p.Id)
            .Select(p => p.PriceAmount)
            .FirstOrDefault()
            ?? service.ServicePrices.OrderByDescending(p => p.Id).Select(p => p.PriceAmount).FirstOrDefault()
            ?? 0m;

        var priceFormatted = activePrice.ToString("N0", new CultureInfo("vi-VN")) + " đ";
        var hospitalIdVal = service.HospitalId.GetValueOrDefault(1);
        var facility = hospitalIdVal > 1 ? $"MedFlow Cơ sở {hospitalIdVal}" : "MedFlow Cơ sở 1";
        var categoryName = ResolveCategory(service.ServiceType);
        var onlineFlag = service.AllowsOnlineBooking ?? false;
        var bookingChannel = onlineFlag ? "ONLINE_ALLOWED" : "COUNTER_ONLY";
        var isActiveFlag = service.IsActive ?? true;
        var statusStr = isActiveFlag ? "ACTIVE" : "SUSPENDED";

        return new ExaminationServiceAdminItemDto(
            Id: service.Id,
            ServiceCode: service.ServiceCode,
            Name: service.Name,
            Description: service.Description,
            ServiceType: service.ServiceType,
            Category: categoryName,
            HospitalId: service.HospitalId,
            Facility: facility,
            DepartmentId: service.DepartmentId,
            DepartmentCode: service.Department?.DepartmentCode,
            DepartmentName: service.Department?.Name,
            SpecialtyId: service.SpecialtyId,
            SpecialtyCode: service.Specialty?.SpecialtyCode,
            SpecialtyName: service.Specialty?.Name,
            Price: activePrice,
            PriceFormatted: priceFormatted,
            AllowsOnlineBooking: onlineFlag,
            BookingChannel: bookingChannel,
            RequiresResultVerification: service.RequiresResultVerification ?? false,
            IsActive: isActiveFlag,
            Status: statusStr
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

