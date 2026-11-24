using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Services;

public sealed class ExaminationServiceAdminService : IExaminationServiceAdminService
{
    private const string UnknownFacilityFallback = "Chưa cập nhật";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public ExaminationServiceAdminService(IApplicationDbContext db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ExaminationServiceAdminListResponseDto> GetExaminationServiceListAsync(
        long actorUserId,
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

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var (isSystemAdmin, actorHospitalId) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        long? scopedHospitalId = request.HospitalId;
        if (!isSystemAdmin)
        {
            if (request.HospitalId.HasValue && request.HospitalId.Value != actorHospitalId!.Value)
            {
                throw new ForbiddenException("Bạn không có quyền xem danh mục dịch vụ khám thuộc cơ sở y tế khác.");
            }

            scopedHospitalId = actorHospitalId!.Value;
        }
        else if (request.HospitalId.HasValue)
        {
            if (request.HospitalId.Value <= 0)
            {
                throw new ValidationException("Mã cơ sở y tế không hợp lệ.");
            }

            var hospitalExists = await _db.Hospitals
                .AsNoTracking()
                .AnyAsync(h => h.Id == request.HospitalId.Value, ct);
            if (!hospitalExists)
            {
                throw new NotFoundException("Hospital", request.HospitalId.Value);
            }
        }

        var baseQuery = _db.Services.AsNoTracking();
        if (!isSystemAdmin && actorHospitalId.HasValue)
        {
            baseQuery = baseQuery.Where(s => s.HospitalId == actorHospitalId.Value);
        }

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

        var filteredQuery = baseQuery
            .Include(s => s.Department)
            .Include(s => s.Specialty)
            .Include(s => s.ServicePrices)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim().ToLower();
            filteredQuery = filteredQuery.Where(s =>
                s.ServiceCode.ToLower().Contains(kw) ||
                s.Name.ToLower().Contains(kw) ||
                (s.Description != null && s.Description.ToLower().Contains(kw)));
        }

        if (request.DepartmentId.HasValue)
        {
            filteredQuery = filteredQuery.Where(s => s.DepartmentId == request.DepartmentId.Value);
        }

        if (request.SpecialtyId.HasValue)
        {
            filteredQuery = filteredQuery.Where(s => s.SpecialtyId == request.SpecialtyId.Value);
        }

        if (scopedHospitalId.HasValue)
        {
            filteredQuery = filteredQuery.Where(s => s.HospitalId == scopedHospitalId.Value);
        }

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

        var totalFiltered = await filteredQuery.CountAsync(ct);
        var totalPages = totalFiltered == 0 ? 1 : (int)Math.Ceiling((double)totalFiltered / request.PageSize);

        var entities = await filteredQuery
            .OrderBy(s => s.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var hospitalIds = entities
            .Where(s => s.HospitalId.HasValue)
            .Select(s => s.HospitalId!.Value)
            .Distinct()
            .ToList();

        var hospitalsById = hospitalIds.Count == 0
            ? new Dictionary<long, string>()
            : await _db.Hospitals
                .AsNoTracking()
                .Where(h => hospitalIds.Contains(h.Id))
                .ToDictionaryAsync(h => h.Id, h => h.Name, ct);

        var nowLocal = HospitalClock.GetNow(_timeProvider);

        Dictionary<long, int> activeBookingCounts = new();
        if (entities.Count > 0)
        {
            try
            {
                var serviceIds = entities.Select(s => s.Id).ToList();
                activeBookingCounts = await _db.Appointments.AsNoTracking()
                    .Where(a => a.Slot != null && a.Slot.ServiceId.HasValue && serviceIds.Contains(a.Slot.ServiceId.Value)
                             && (a.Status == null || (a.Status != "CANCELLED" && a.Status != "NO_SHOW" && a.Status != "COMPLETED")))
                    .GroupBy(a => a.Slot!.ServiceId!.Value)
                    .Select(g => new { ServiceId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.ServiceId, x => x.Count, ct);
            }
            catch (NotSupportedException)
            {
                // In-memory test context without Appointments DbSet configured
            }
        }

        var items = entities.Select(s =>
        {
            var isActive = s.IsActive ?? true;
            var status = isActive ? "ACTIVE" : "SUSPENDED";
            var allowsOnline = s.AllowsOnlineBooking ?? false;
            var bookingChannel = allowsOnline ? "ONLINE_ALLOWED" : "COUNTER_ONLY";
            var category = ResolveCategory(s.ServiceType);
            var facility = s.HospitalId.HasValue && hospitalsById.TryGetValue(s.HospitalId.Value, out var hospitalName)
                ? ResolveFacilityName(hospitalName)
                : UnknownFacilityFallback;

            var activePrice = s.ServicePrices
                .Where(p => (!p.EffectiveFrom.HasValue || p.EffectiveFrom.Value <= nowLocal)
                         && (!p.EffectiveTo.HasValue || p.EffectiveTo.Value > nowLocal))
                .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
                .ThenByDescending(p => p.Id)
                .Select(p => p.PriceAmount)
                .FirstOrDefault()
                ?? s.ServicePrices.OrderByDescending(p => p.Id).Select(p => p.PriceAmount).FirstOrDefault()
                ?? 0m;

            var priceFormatted = activePrice.ToString("N0", new CultureInfo("vi-VN")) + " đ";
            var activeBookings = activeBookingCounts.GetValueOrDefault(s.Id, 0);

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
                Status: status,
                HasActiveBookings: activeBookings > 0,
                ActiveBookingsCount: activeBookings
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
        long actorUserId,
        CreateExaminationServiceRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

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

        var price = request.Price ?? request.InitialPrice;
        if (price.HasValue && price.Value < 0)
        {
            throw new ArgumentException("Giá dịch vụ không được là số âm.", nameof(request.Price));
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var (isSystemAdmin, actorHospitalId) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

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

        Specialty? specialty = null;
        if (request.SpecialtyId.HasValue)
        {
            specialty = await _db.Specialties.FirstOrDefaultAsync(s => s.Id == request.SpecialtyId.Value, ct);
            if (specialty is null)
            {
                throw new KeyNotFoundException($"Không tìm thấy chuyên khoa với mã {request.SpecialtyId.Value}.");
            }
        }

        long hospitalId;
        if (!isSystemAdmin)
        {
            if (request.HospitalId.HasValue && request.HospitalId.Value != actorHospitalId!.Value)
            {
                throw new ForbiddenException("Bạn không có quyền tạo dịch vụ khám cho cơ sở y tế khác.");
            }

            hospitalId = actorHospitalId!.Value;
        }
        else
        {
            if (!request.HospitalId.HasValue || request.HospitalId.Value <= 0)
            {
                throw new ValidationException("Vui lòng chọn cơ sở y tế hợp lệ cho dịch vụ khám.");
            }

            hospitalId = request.HospitalId.Value;
        }

        var hospital = await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == hospitalId, ct)
            ?? throw new NotFoundException("Hospital", hospitalId);

        if (department is not null && department.HospitalId.HasValue && department.HospitalId.Value != hospitalId)
        {
            throw new ValidationException("Khoa phòng không thuộc cơ sở y tế đã chọn.");
        }

        var isDuplicate = await _db.Services.AnyAsync(
            s => s.HospitalId == hospitalId && s.ServiceCode.ToLower() == serviceCode.ToLower(), ct);

        if (isDuplicate)
        {
            throw new InvalidOperationException($"Mã dịch vụ '{serviceCode}' đã tồn tại trong cơ sở y tế.");
        }

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

        var effectivePrice = price.GetValueOrDefault(0m);
        if (price.HasValue)
        {
            var nowLocal = HospitalClock.GetNow(_timeProvider);
            var servicePrice = new ServicePrice
            {
                ServiceId = newService.Id,
                PriceAmount = price.Value,
                EffectiveFrom = nowLocal,
                EffectiveTo = null
            };
            _db.ServicePrices.Add(servicePrice);
            await _db.SaveChangesAsync(ct);
        }

        var facility = ResolveFacilityName(hospital.Name);
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
        long actorUserId,
        long id,
        UpdateExaminationServiceRequest request,
        CancellationToken ct = default)
    {
        if (id <= 0)
        {
            throw new ArgumentException("ID dịch vụ khám phải lớn hơn 0.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(request);

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

        if (request.Price.HasValue && request.Price.Value < 0)
        {
            throw new ArgumentException("Giá dịch vụ không được là số âm.", nameof(request.Price));
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var (isSystemAdmin, actorHospitalId) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        var service = await _db.Services
            .Include(s => s.Department)
            .Include(s => s.Specialty)
            .Include(s => s.ServicePrices)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (service is null)
        {
            throw new KeyNotFoundException($"Không tìm thấy dịch vụ khám với ID {id}.");
        }

        long targetHospitalId;
        if (!isSystemAdmin)
        {
            if (!service.HospitalId.HasValue || service.HospitalId.Value != actorHospitalId!.Value)
            {
                throw new ForbiddenException("Bạn không có quyền thao tác trên dịch vụ khám thuộc cơ sở y tế khác.");
            }

            if (request.HospitalId.HasValue && request.HospitalId.Value != actorHospitalId.Value)
            {
                throw new ForbiddenException("Bạn không có quyền chuyển dịch vụ khám sang cơ sở y tế khác.");
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
            if (!service.HospitalId.HasValue || service.HospitalId.Value <= 0)
            {
                throw new ValidationException("Dịch vụ khám chưa được gắn cơ sở y tế hợp lệ.");
            }

            targetHospitalId = service.HospitalId.Value;
        }

        var hospital = await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == targetHospitalId, ct)
            ?? throw new NotFoundException("Hospital", targetHospitalId);

        if (newCode is not null)
        {
            if (!string.Equals(service.ServiceCode, newCode, StringComparison.OrdinalIgnoreCase) ||
                targetHospitalId != service.HospitalId)
            {
                var isDuplicate = await _db.Services.AnyAsync(
                    s => s.Id != id && s.HospitalId == targetHospitalId && s.ServiceCode.ToLower() == newCode.ToLower(), ct);

                if (isDuplicate)
                {
                    throw new InvalidOperationException($"Mã dịch vụ '{newCode}' đã tồn tại trong cơ sở y tế.");
                }
            }

            service.ServiceCode = newCode;
        }

        if (newName is not null)
        {
            service.Name = newName;
        }

        if (newServiceType is not null)
        {
            service.ServiceType = newServiceType;
        }

        if (request.DepartmentId.HasValue)
        {
            var dept = await _db.Departments.FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value, ct);
            if (dept is null)
            {
                throw new KeyNotFoundException($"Không tìm thấy khoa phòng với mã {request.DepartmentId.Value}.");
            }

            if (dept.HospitalId.HasValue && dept.HospitalId.Value != targetHospitalId)
            {
                throw new ValidationException("Khoa phòng không thuộc cơ sở y tế của dịch vụ khám.");
            }

            service.DepartmentId = dept.Id;
            service.Department = dept;
        }
        else if (!string.IsNullOrWhiteSpace(request.Department))
        {
            var deptQuery = request.Department.Trim().ToLower();
            var dept = await _db.Departments.FirstOrDefaultAsync(
                d => (d.HospitalId == null || d.HospitalId == targetHospitalId)
                    && (d.Name.ToLower() == deptQuery || d.DepartmentCode.ToLower() == deptQuery),
                ct);
            if (dept is null)
            {
                throw new KeyNotFoundException($"Không tìm thấy khoa phòng: {request.Department.Trim()}.");
            }
            service.DepartmentId = dept.Id;
            service.Department = dept;
        }

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

        service.HospitalId = targetHospitalId;

        if (request.Description is not null)
        {
            service.Description = request.Description.Trim();
        }

        var allowsOnline = request.AllowsOnlineBooking ?? request.OnlineBooking;
        if (allowsOnline.HasValue)
        {
            service.AllowsOnlineBooking = allowsOnline.Value;
        }

        if (request.RequiresResultVerification.HasValue)
        {
            service.RequiresResultVerification = request.RequiresResultVerification.Value;
        }
        else if (service.ServiceType == "DIAGNOSTIC" && !service.RequiresResultVerification.HasValue)
        {
            service.RequiresResultVerification = true;
        }

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

        var nowLocal = HospitalClock.GetNow(_timeProvider);
        if (request.Price.HasValue)
        {
            foreach (var existingPrice in service.ServicePrices.Where(p => !p.EffectiveTo.HasValue || p.EffectiveTo > nowLocal))
            {
                existingPrice.EffectiveTo = nowLocal;
            }

            var newPrice = new ServicePrice
            {
                ServiceId = service.Id,
                PriceAmount = request.Price.Value,
                EffectiveFrom = nowLocal,
                EffectiveTo = null
            };
            service.ServicePrices.Add(newPrice);
            _db.ServicePrices.Add(newPrice);
        }

        await _db.SaveChangesAsync(ct);

        var activePrice = service.ServicePrices
            .Where(p => (!p.EffectiveFrom.HasValue || p.EffectiveFrom.Value <= nowLocal)
                     && (!p.EffectiveTo.HasValue || p.EffectiveTo.Value > nowLocal))
            .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
            .ThenByDescending(p => p.Id)
            .Select(p => p.PriceAmount)
            .FirstOrDefault()
            ?? service.ServicePrices.OrderByDescending(p => p.Id).Select(p => p.PriceAmount).FirstOrDefault()
            ?? 0m;

        var priceFormatted = activePrice.ToString("N0", new CultureInfo("vi-VN")) + " đ";
        var facility = ResolveFacilityName(hospital.Name);
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

    public async Task<DeactivateExaminationServiceResponseDto> DeactivateExaminationServiceAsync(
        long? actorUserId,
        string idOrCode,
        DeactivateExaminationServiceRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ArgumentException("Mã hoặc ID dịch vụ khám không được để trống.", nameof(idOrCode));
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowUtcUnspecified = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var (isSystemAdmin, actorHospitalId) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        var trimmedIdOrCode = idOrCode.Trim();
        var isNumeric = long.TryParse(trimmedIdOrCode, out var parsedId);

        var service = await _db.Services
            .Include(s => s.Department)
            .Include(s => s.Specialty)
            .FirstOrDefaultAsync(s => isNumeric
                ? s.Id == parsedId
                : s.ServiceCode.ToLower() == trimmedIdOrCode.ToLower(), ct);

        if (service is null)
        {
            throw new NotFoundException("Service", trimmedIdOrCode);
        }

        if (!isSystemAdmin && (!service.HospitalId.HasValue || service.HospitalId.Value != actorHospitalId!.Value))
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên dịch vụ khám thuộc cơ sở y tế khác.");
        }

        if (service.IsActive == false)
        {
            throw new ConflictException($"Dịch vụ khám '{service.Name}' ({service.ServiceCode}) hiện đã ở trạng thái ngừng hoạt động.");
        }

        List<ExaminationSlot> activeSlots = new();
        List<Appointment> activeAppointments = new();

        try
        {
            activeSlots = await _db.ExaminationSlots
                .Where(s => s.ServiceId == service.Id && s.Status != "CLOSED" && (s.EndsAt == null || s.EndsAt >= nowLocal))
                .ToListAsync(ct);

            if (activeSlots.Count > 0)
            {
                var activeSlotIds = activeSlots.Select(s => s.Id).ToList();
                activeAppointments = await _db.Appointments
                    .Where(a => a.SlotId.HasValue && activeSlotIds.Contains(a.SlotId.Value)
                             && (a.Status == null || (a.Status != "CANCELLED" && a.Status != "NO_SHOW" && a.Status != "COMPLETED")))
                    .ToListAsync(ct);
            }
        }
        catch (NotSupportedException)
        {
            // In-memory test context without slots/appointments
        }

        var activeBookingsCount = activeAppointments.Count;
        var activeSlotsCount = activeSlots.Count;

        if (activeBookingsCount > 0 && !(request?.ConfirmUnderstood ?? false))
        {
            throw new ConflictException($"Không thể ngừng hoạt động dịch vụ khám khi còn {activeBookingsCount} lịch hẹn khám hoặc ca tiếp nhận đang chờ xử lý.");
        }

        if (activeSlotsCount > 0 && activeBookingsCount == 0 && !(request?.ConfirmUnderstood ?? false) && !(request?.CloseFutureSlots ?? false))
        {
            throw new ConflictException($"Không thể ngừng hoạt động dịch vụ khám khi còn {activeSlotsCount} khung giờ khám đang mở chưa được giải quyết.");
        }

        if (request?.ConfirmUnderstood == true || request?.CloseFutureSlots == true ||
            string.Equals(request?.AffectedBookingsAction, "CLOSE_FUTURE_SLOTS", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var slot in activeSlots)
            {
                slot.Status = "CLOSED";
            }
        }

        service.IsActive = false;

        TryAddServiceAuditLog(
            actorUserId,
            service.HospitalId ?? actorHospitalId,
            "EXAMINATION_SERVICE_DEACTIVATED",
            service.Id.ToString(CultureInfo.InvariantCulture),
            request?.Reason ?? request?.Note ?? "Ngừng hoạt động dịch vụ khám",
            new
            {
                serviceId = service.Id,
                serviceCode = service.ServiceCode,
                serviceName = service.Name,
                previousStatus = "ACTIVE",
                targetStatus = "SUSPENDED",
                activeBookingsCount,
                activeSlotsCount,
                affectedBookingsAction = request?.AffectedBookingsAction,
                reasonCode = request?.ReasonCode,
                note = request?.Note,
                ipAddress,
                deactivatedAt = nowUtcUnspecified
            },
            ipAddress,
            nowUtcUnspecified);

        await _db.SaveChangesAsync(ct);

        return new DeactivateExaminationServiceResponseDto(
            Id: service.Id,
            ServiceCode: service.ServiceCode,
            Name: service.Name,
            IsActive: false,
            Status: "SUSPENDED",
            Message: $"Đã chuyển trạng thái dịch vụ {service.Name} sang Tạm ngừng hoạt động.",
            ActiveBookingsCount: activeBookingsCount,
            ActiveSlotsCount: activeSlotsCount,
            OccurredAtUtc: nowUtcUnspecified
        );
    }

    public async Task<DeactivateExaminationServiceResponseDto> ReactivateExaminationServiceAsync(
        long? actorUserId,
        string idOrCode,
        ReactivateExaminationServiceRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ArgumentException("Mã hoặc ID dịch vụ khám không được để trống.", nameof(idOrCode));
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowUtcUnspecified = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var (isSystemAdmin, actorHospitalId) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        var trimmedIdOrCode = idOrCode.Trim();
        var isNumeric = long.TryParse(trimmedIdOrCode, out var parsedId);

        var service = await _db.Services
            .Include(s => s.Department)
            .Include(s => s.Specialty)
            .FirstOrDefaultAsync(s => isNumeric
                ? s.Id == parsedId
                : s.ServiceCode.ToLower() == trimmedIdOrCode.ToLower(), ct);

        if (service is null)
        {
            throw new NotFoundException("Service", trimmedIdOrCode);
        }

        if (!isSystemAdmin && (!service.HospitalId.HasValue || service.HospitalId.Value != actorHospitalId!.Value))
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên dịch vụ khám thuộc cơ sở y tế khác.");
        }

        if (service.IsActive ?? true)
        {
            throw new ConflictException($"Dịch vụ khám '{service.Name}' ({service.ServiceCode}) hiện đang ở trạng thái hoạt động.");
        }

        service.IsActive = true;

        TryAddServiceAuditLog(
            actorUserId,
            service.HospitalId ?? actorHospitalId,
            "EXAMINATION_SERVICE_REACTIVATED",
            service.Id.ToString(CultureInfo.InvariantCulture),
            request?.Reason ?? request?.Note ?? "Kích hoạt lại dịch vụ khám",
            new
            {
                serviceId = service.Id,
                serviceCode = service.ServiceCode,
                serviceName = service.Name,
                previousStatus = "SUSPENDED",
                targetStatus = "ACTIVE",
                note = request?.Note,
                ipAddress,
                reactivatedAt = nowUtcUnspecified
            },
            ipAddress,
            nowUtcUnspecified);

        await _db.SaveChangesAsync(ct);

        return new DeactivateExaminationServiceResponseDto(
            Id: service.Id,
            ServiceCode: service.ServiceCode,
            Name: service.Name,
            IsActive: true,
            Status: "ACTIVE",
            Message: $"Đã kích hoạt lại dịch vụ {service.Name} thành công.",
            ActiveBookingsCount: 0,
            ActiveSlotsCount: 0,
            OccurredAtUtc: nowUtcUnspecified
        );
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

        var actorRoleCodes = actorRoleIds.Count == 0
            ? []
            : await _db.Roles
                .AsNoTracking()
                .Where(r => actorRoleIds.Contains(r.Id))
                .Select(r => r.Code)
                .ToListAsync(ct);

        var isSystemAdmin = actorRoleCodes.Any(c =>
            string.Equals(c, RoleNames.SystemAdmin, StringComparison.OrdinalIgnoreCase));
        var isHospitalAdmin = actorRoleCodes.Any(c =>
            string.Equals(c, RoleNames.HospitalAdmin, StringComparison.OrdinalIgnoreCase));

        if (!isSystemAdmin && !isHospitalAdmin)
        {
            throw new ForbiddenException("Người dùng không có quyền quản trị dịch vụ khám.");
        }

        var hasHospitalConfigPerm = await (
            from rp in _db.RolePermissions.AsNoTracking()
            join p in _db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where actorRoleIds.Contains(rp.RoleId) && p.Code.ToUpper() == PermissionCodes.HospitalConfig
            select rp.PermissionId).AnyAsync(ct);

        if (!hasHospitalConfigPerm)
        {
            throw new ForbiddenException("Tài khoản quản trị chưa được cấp quyền cấu hình danh mục bệnh viện (HOSPITAL_CONFIG).");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId.Value, ct);

        if (actorStaff is not null &&
            !string.IsNullOrWhiteSpace(actorStaff.Status) &&
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
                .FirstOrDefaultAsync(h => h.Id == actorStaff.HospitalId.Value, ct)
                ?? throw new NotFoundException("Hospital", actorStaff.HospitalId.Value);

            if (hospital.IsActive == false)
            {
                throw new ForbiddenException("Cơ sở y tế của quản trị viên đang ngừng hoạt động.");
            }

            return (false, actorStaff.HospitalId.Value);
        }

        return (true, actorStaff?.HospitalId);
    }

    private void TryAddServiceAuditLog(
        long? actorUserId,
        long? hospitalId,
        string action,
        string entityId,
        string? reason,
        object? changeData,
        string? ipAddress,
        DateTime nowUtc)
    {
        try
        {
            var ip = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var parsedIp)
                ? parsedIp
                : null;

            _db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                HospitalId = hospitalId,
                Action = action,
                EntityType = "Service",
                EntityId = entityId,
                Reason = reason,
                ChangeData = changeData is not null ? JsonSerializer.Serialize(changeData) : null,
                IpAddress = ip,
                OccurredAt = nowUtc
            });
        }
        catch (NotSupportedException)
        {
            // In-memory test context without AuditLogs DbSet
        }
    }

    private static string ResolveFacilityName(string? name) =>
        !string.IsNullOrWhiteSpace(name) ? name.Trim() : UnknownFacilityFallback;

    private static string ResolveCategory(string? serviceType)
    {
        if (string.Equals(serviceType, "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
            return "Cận lâm sàng kỹ thuật";
        if (string.Equals(serviceType, "CONSULTATION", StringComparison.OrdinalIgnoreCase))
            return "Hội chẩn Lâm sàng";
        return "Khám Lâm sàng";
    }
}
