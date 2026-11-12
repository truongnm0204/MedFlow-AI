using System.Globalization;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Services;

public sealed class DiagnosticServiceAdminService : IDiagnosticServiceAdminService
{
    private const string HospitalAdminRoleCode = "HOSPITAL_ADMIN";
    private const string DiagnosticServiceType = "DIAGNOSTIC";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public DiagnosticServiceAdminService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DiagnosticServiceAdminListResponseDto> GetDiagnosticServiceListAsync(
        long actorUserId,
        GetDiagnosticServiceListAdminRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Page < 1)
        {
            throw new ValidationException("Trang yêu cầu phải lớn hơn hoặc bằng 1.");
        }

        if (request.PageSize < 1 || request.PageSize > 100)
        {
            throw new ValidationException("Số lượng bản ghi trên một trang phải từ 1 đến 100.");
        }

        var normalizedKeyword = string.IsNullOrWhiteSpace(request.Keyword)
            ? null
            : request.Keyword.Trim();

        if (normalizedKeyword is not null && normalizedKeyword.Length > 100)
        {
            throw new ValidationException("Từ khóa tìm kiếm tối đa 100 ký tự.");
        }

        if (request.DepartmentId is <= 0)
        {
            throw new ValidationException("Mã khoa phòng lọc không hợp lệ.");
        }

        if (request.SpecialtyId is <= 0)
        {
            throw new ValidationException("Mã chuyên khoa lọc không hợp lệ.");
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var statusCandidate = request.Status.Trim().ToUpperInvariant();
            if (statusCandidate is "ACTIVE" or "INACTIVE")
            {
                normalizedStatus = statusCandidate;
            }
            else if (statusCandidate != "ALL")
            {
                throw new ValidationException("Trạng thái dịch vụ cận lâm sàng lọc không hợp lệ.");
            }
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var (hospital, hospitalId) = await EnsureHospitalAdminAccessAsync(actorUserId, nowUtc, ct);

        var scoped = _db.Services
            .AsNoTracking()
            .Where(s => s.HospitalId == hospitalId && s.ServiceType.ToUpper() == DiagnosticServiceType);

        var scopedServices = await scoped
            .Select(s => new
            {
                s.Id,
                s.DepartmentId,
                s.IsActive,
                s.RequiresResultVerification
            })
            .ToListAsync(ct);

        var totalAll = scopedServices.Count;
        var activeCount = scopedServices.Count(s => s.IsActive ?? true);
        var inactiveCount = totalAll - activeCount;
        var requiresVerificationCount = scopedServices.Count(s => s.RequiresResultVerification ?? false);
        var departmentsCount = scopedServices
            .Select(s => s.DepartmentId)
            .Where(d => d.HasValue)
            .Distinct()
            .Count();

        var scopedIds = scopedServices.Select(s => s.Id).ToList();

        var diagPoints = scopedIds.Count == 0
            ? new List<QueueServicePoint>()
            : await _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp => sp.PointType != null
                          && sp.PointType.ToUpper() == DiagnosticServiceType
                          && sp.ServiceId != null
                          && scopedIds.Contains(sp.ServiceId.Value)
                          && (sp.HospitalId == hospitalId || sp.HospitalId == null))
                .ToListAsync(ct);

        var activeServicePointsCount = diagPoints.Count(sp => sp.IsActive ?? true);

        var metrics = new DiagnosticServiceAdminMetricsDto(
            Total: totalAll,
            Active: activeCount,
            Inactive: inactiveCount,
            RequiresVerificationCount: requiresVerificationCount,
            DepartmentsCount: departmentsCount,
            ActiveServicePointsCount: activeServicePointsCount);

        var filteredQuery = scoped
            .Include(s => s.Department)
            .Include(s => s.Specialty)
            .Include(s => s.ServicePrices)
            .AsQueryable();

        if (normalizedKeyword is not null)
        {
            var kw = normalizedKeyword.ToLower();
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

        if (normalizedStatus == "ACTIVE")
        {
            filteredQuery = filteredQuery.Where(s => s.IsActive != false);
        }
        else if (normalizedStatus == "INACTIVE")
        {
            filteredQuery = filteredQuery.Where(s => s.IsActive == false);
        }

        if (request.RequiresResultVerification.HasValue)
        {
            if (request.RequiresResultVerification.Value)
            {
                filteredQuery = filteredQuery.Where(s => s.RequiresResultVerification == true);
            }
            else
            {
                filteredQuery = filteredQuery.Where(s => s.RequiresResultVerification != true);
            }
        }

        var totalFiltered = await filteredQuery.CountAsync(ct);
        var totalPages = totalFiltered == 0 ? 1 : (int)Math.Ceiling((double)totalFiltered / request.PageSize);

        var entities = await filteredQuery
            .OrderBy(s => s.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var pageServiceIds = entities.Select(s => s.Id).ToHashSet();
        var roomIds = diagPoints
            .Where(sp => sp.ServiceId.HasValue && pageServiceIds.Contains(sp.ServiceId.Value) && sp.RoomId.HasValue)
            .Select(sp => sp.RoomId!.Value)
            .Distinct()
            .ToList();

        var roomsById = roomIds.Count == 0
            ? new Dictionary<long, HospitalRoom>()
            : await _db.HospitalRooms
                .AsNoTracking()
                .Where(r => roomIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, ct);

        var now = DateTime.SpecifyKind(nowUtc, DateTimeKind.Unspecified);

        var items = entities.Select(s =>
        {
            var isActive = s.IsActive ?? true;
            var status = isActive ? "ACTIVE" : "INACTIVE";

            var activePrice = s.ServicePrices
                .Where(p => (!p.EffectiveFrom.HasValue || p.EffectiveFrom.Value <= now)
                         && (!p.EffectiveTo.HasValue || p.EffectiveTo.Value > now))
                .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
                .ThenByDescending(p => p.Id)
                .Select(p => p.PriceAmount)
                .FirstOrDefault()
                ?? s.ServicePrices
                    .OrderByDescending(p => p.Id)
                    .Select(p => p.PriceAmount)
                    .FirstOrDefault();

            var priceFormatted = activePrice.HasValue
                ? activePrice.Value.ToString("N0", new CultureInfo("vi-VN")) + " đ"
                : null;

            var servicePoints = diagPoints
                .Where(sp => sp.ServiceId == s.Id)
                .OrderBy(sp => sp.Id)
                .Select(sp =>
                {
                    HospitalRoom? room = null;
                    if (sp.RoomId.HasValue)
                    {
                        roomsById.TryGetValue(sp.RoomId.Value, out room);
                    }

                    return new DiagnosticServiceAdminServicePointDto(
                        ServicePointId: sp.Id,
                        Name: sp.Name,
                        RoomId: sp.RoomId,
                        RoomCode: room?.RoomCode,
                        RoomName: room?.Name,
                        IsActive: sp.IsActive ?? true);
                })
                .ToList();

            var servicePointCount = servicePoints.Count;
            var activeServicePointCount = servicePoints.Count(sp => sp.IsActive);

            return new DiagnosticServiceAdminItemDto(
                Id: s.Id,
                ServiceCode: s.ServiceCode,
                Name: s.Name,
                Description: s.Description,
                ServiceType: s.ServiceType,
                DepartmentId: s.DepartmentId,
                DepartmentCode: s.Department?.DepartmentCode,
                DepartmentName: s.Department?.Name,
                SpecialtyId: s.SpecialtyId,
                SpecialtyCode: s.Specialty?.SpecialtyCode,
                SpecialtyName: s.Specialty?.Name,
                Price: activePrice,
                PriceFormatted: priceFormatted,
                RequiresResultVerification: s.RequiresResultVerification ?? false,
                IsActive: isActive,
                Status: status,
                ServicePointCount: servicePointCount,
                ActiveServicePointCount: activeServicePointCount,
                ServicePoints: servicePoints);
        }).ToList();

        return new DiagnosticServiceAdminListResponseDto(
            HospitalId: hospitalId,
            HospitalCode: hospital.HospitalCode,
            HospitalName: hospital.Name,
            Data: items,
            Total: totalFiltered,
            Page: request.Page,
            PageSize: request.PageSize,
            TotalPages: totalPages,
            Metrics: metrics);
    }

    private async Task<(Hospital Hospital, long HospitalId)> EnsureHospitalAdminAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var actorUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (actorUser.AccountStatus != AccountStatus.ACTIVE)
        {
            throw new ForbiddenException("Tài khoản quản trị bệnh viện không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản quản trị bệnh viện đang bị tạm khóa.");
        }

        var allRoles = await _db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var actorRoleIds = (await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct))
            .ToHashSet();

        var actorRoleCodes = allRoles
            .Where(r => actorRoleIds.Contains(r.Id))
            .Select(r => r.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        if (!actorRoleCodes.Any(c => string.Equals(c, HospitalAdminRoleCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Người dùng không có quyền xem danh mục dịch vụ cận lâm sàng.");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is null || !actorStaff.HospitalId.HasValue)
        {
            throw new ForbiddenException("Tài khoản quản trị bệnh viện chưa được gán cơ sở y tế.");
        }

        if (!string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của quản trị viên bệnh viện đã ngừng hoạt động.");
        }

        var hospitalId = actorStaff.HospitalId.Value;

        var hospital = await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == hospitalId, ct)
            ?? throw new NotFoundException("Hospital", hospitalId);

        if (hospital.IsActive == false)
        {
            throw new ForbiddenException("Cơ sở y tế được gán cho quản trị viên hiện đang ngừng hoạt động.");
        }

        return (hospital, hospitalId);
    }
}
