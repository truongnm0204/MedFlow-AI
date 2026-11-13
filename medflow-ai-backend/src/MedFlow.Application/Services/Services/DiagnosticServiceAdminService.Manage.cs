using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Services;

public sealed partial class DiagnosticServiceAdminService
{
    private const string DiagnosticServiceEntityType = "SERVICE";
    private const string HospitalConfigPermissionCode = "HOSPITAL_CONFIG";
    private const string DiagnosticServiceCreatedAction = "DIAGNOSTIC_SERVICE_CREATED";
    private const string DiagnosticServiceUpdatedAction = "DIAGNOSTIC_SERVICE_UPDATED";
    private const string DiagnosticServiceDeactivatedAction = "DIAGNOSTIC_SERVICE_DEACTIVATED";
    private const decimal MaxPrice = 1_000_000_000m;

    private static readonly Regex ServiceCodeRegex = new(
        @"^[A-Z0-9][A-Z0-9_.-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] ClosedDiagnosticOrderStatuses =
    [
        "COMPLETED",
        "CANCELLED",
        "DONE",
        "FINALIZED",
        "VERIFIED",
    ];

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<DiagnosticServiceFormOptionsDto> GetFormOptionsAsync(
        long actorUserId,
        CancellationToken ct = default)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var (_, hospitalId) = await EnsureHospitalConfigWriteAccessAsync(actorUserId, nowUtc, ct);

        var departments = await _db.Departments
            .AsNoTracking()
            .Where(d => d.HospitalId == hospitalId && d.IsActive != false)
            .OrderBy(d => d.Name)
            .Select(d => new DiagnosticServiceDepartmentOptionDto(d.Id, d.DepartmentCode, d.Name))
            .ToListAsync(ct);

        var specialties = await _db.Specialties
            .AsNoTracking()
            .Where(s => s.IsActive != false)
            .OrderBy(s => s.Name)
            .Select(s => new DiagnosticServiceSpecialtyOptionDto(s.Id, s.SpecialtyCode, s.Name))
            .ToListAsync(ct);

        return new DiagnosticServiceFormOptionsDto(hospitalId, departments, specialties);
    }

    public async Task<DiagnosticServiceAdminItemDto> CreateDiagnosticServiceAsync(
        long actorUserId,
        CreateDiagnosticServiceAdminRequest? request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var fields = ValidateCreate(request);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var (_, hospitalId) = await EnsureHospitalConfigWriteAccessAsync(actorUserId, nowUtc, ct);
        await EnsureReferencesAsync(hospitalId, fields.DepartmentId, fields.SpecialtyId, null, null, ct);

        var codeTaken = await _db.Services.AnyAsync(
            s => s.HospitalId == hospitalId && s.ServiceCode.ToUpper() == fields.ServiceCode,
            ct);
        if (codeTaken)
        {
            throw new ConflictException($"Mã dịch vụ '{fields.ServiceCode}' đã tồn tại trong cơ sở.");
        }

        var entity = new Service
        {
            HospitalId = hospitalId,
            DepartmentId = fields.DepartmentId,
            SpecialtyId = fields.SpecialtyId,
            ServiceCode = fields.ServiceCode!,
            Name = fields.Name,
            ServiceType = DiagnosticServiceType,
            Description = fields.Description,
            AllowsOnlineBooking = false,
            RequiresResultVerification = fields.RequiresResultVerification ?? true,
            IsActive = true,
        };

        await using var tx = await _db.BeginTransactionAsync(ct);
        _db.Services.Add(entity);
        if (fields.Price is decimal price)
        {
            _db.ServicePrices.Add(new ServicePrice
            {
                Service = entity,
                PriceAmount = price,
                EffectiveFrom = DateTime.SpecifyKind(nowUtc, DateTimeKind.Unspecified),
                EffectiveTo = null,
            });
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            throw new ConflictException($"Mã dịch vụ '{fields.ServiceCode}' đã tồn tại trong cơ sở.");
        }

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = hospitalId,
            Action = DiagnosticServiceCreatedAction,
            EntityType = DiagnosticServiceEntityType,
            EntityId = entity.Id.ToString(CultureInfo.InvariantCulture),
            Reason = "Quản trị bệnh viện tạo dịch vụ cận lâm sàng",
            ChangeData = JsonSerializer.Serialize(new
            {
                serviceId = entity.Id,
                serviceCode = entity.ServiceCode,
                name = entity.Name,
                departmentId = entity.DepartmentId,
                specialtyId = entity.SpecialtyId,
                requiresResultVerification = entity.RequiresResultVerification,
                price = fields.Price,
                ipAddress,
            }, AuditJsonOptions),
            IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
            OccurredAt = nowUtc,
        });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await LoadItemAsync(hospitalId, entity.Id, nowUtc, ct);
    }

    public async Task<DiagnosticServiceAdminItemDto> UpdateDiagnosticServiceAsync(
        long actorUserId,
        long serviceId,
        UpdateDiagnosticServiceAdminRequest? request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        if (serviceId <= 0)
        {
            throw new ValidationException("Mã dịch vụ cận lâm sàng không hợp lệ.");
        }

        var fields = ValidateUpdate(request);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var (_, hospitalId) = await EnsureHospitalConfigWriteAccessAsync(actorUserId, nowUtc, ct);
        var service = await FindScopedServiceForUpdateAsync(hospitalId, serviceId, ct);
        if (service.IsActive == false)
        {
            throw new ConflictException("Không thể cập nhật dịch vụ cận lâm sàng đang ngừng hoạt động.");
        }

        await EnsureReferencesAsync(
            hospitalId,
            fields.DepartmentId,
            fields.SpecialtyId,
            service.DepartmentId,
            service.SpecialtyId,
            ct);

        var before = new
        {
            name = service.Name,
            description = service.Description,
            departmentId = service.DepartmentId,
            specialtyId = service.SpecialtyId,
            requiresResultVerification = service.RequiresResultVerification,
        };

        service.Name = fields.Name;
        service.Description = fields.Description;
        service.DepartmentId = fields.DepartmentId;
        service.SpecialtyId = fields.SpecialtyId;
        service.RequiresResultVerification = fields.RequiresResultVerification ?? service.RequiresResultVerification;

        var after = new
        {
            name = service.Name,
            description = service.Description,
            departmentId = service.DepartmentId,
            specialtyId = service.SpecialtyId,
            requiresResultVerification = service.RequiresResultVerification,
        };

        await using var tx = await _db.BeginTransactionAsync(ct);
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = hospitalId,
            Action = DiagnosticServiceUpdatedAction,
            EntityType = DiagnosticServiceEntityType,
            EntityId = service.Id.ToString(CultureInfo.InvariantCulture),
            Reason = "Quản trị bệnh viện cập nhật dịch vụ cận lâm sàng",
            ChangeData = JsonSerializer.Serialize(new
            {
                serviceId = service.Id,
                serviceCode = service.ServiceCode,
                before,
                after,
                ipAddress,
            }, AuditJsonOptions),
            IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
            OccurredAt = nowUtc,
        });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await LoadItemAsync(hospitalId, service.Id, nowUtc, ct);
    }

    public async Task<DiagnosticServiceAdminItemDto> DeactivateDiagnosticServiceAsync(
        long actorUserId,
        long serviceId,
        DeactivateDiagnosticServiceAdminRequest? request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        if (serviceId <= 0)
        {
            throw new ValidationException("Mã dịch vụ cận lâm sàng không hợp lệ.");
        }

        var reason = request?.Reason?.Trim() ?? string.Empty;
        if (request is null || reason.Length < 5 || reason.Length > 500)
        {
            throw new ValidationException("Lý do ngừng hoạt động phải từ 5 đến 500 ký tự.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var (_, hospitalId) = await EnsureHospitalConfigWriteAccessAsync(actorUserId, nowUtc, ct);
        var service = await FindScopedServiceForUpdateAsync(hospitalId, serviceId, ct);
        if (service.IsActive == false)
        {
            throw new ConflictException("Dịch vụ cận lâm sàng đã ngừng hoạt động trước đó.");
        }

        var openOrderCount = await _db.DiagnosticOrders.CountAsync(
            o => o.ServiceId == serviceId &&
                 (o.Status == null || !ClosedDiagnosticOrderStatuses.Contains(o.Status.ToUpper())),
            ct);
        if (openOrderCount > 0)
        {
            throw new ConflictException(
                $"Không thể ngừng hoạt động: còn {openOrderCount} phiếu chỉ định cận lâm sàng chưa hoàn tất sử dụng dịch vụ này.");
        }

        var activeServicePointCount = await _db.QueueServicePoints.CountAsync(
            sp => sp.ServiceId == serviceId
                  && sp.PointType != null
                  && sp.PointType.ToUpper() == DiagnosticServiceType
                  && (sp.HospitalId == hospitalId || sp.HospitalId == null)
                  && sp.IsActive != false,
            ct);

        service.IsActive = false;

        await using var tx = await _db.BeginTransactionAsync(ct);
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = hospitalId,
            Action = DiagnosticServiceDeactivatedAction,
            EntityType = DiagnosticServiceEntityType,
            EntityId = service.Id.ToString(CultureInfo.InvariantCulture),
            Reason = reason,
            ChangeData = JsonSerializer.Serialize(new
            {
                serviceId = service.Id,
                serviceCode = service.ServiceCode,
                previousStatus = "ACTIVE",
                status = "INACTIVE",
                activeServicePointCount,
                ipAddress,
            }, AuditJsonOptions),
            IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
            OccurredAt = nowUtc,
        });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await LoadItemAsync(hospitalId, service.Id, nowUtc, ct);
    }

    private async Task<(Hospital Hospital, long HospitalId)> EnsureHospitalConfigWriteAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var access = await EnsureHospitalAdminAccessAsync(actorUserId, nowUtc, ct);

        var roleIds = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var permissionIds = roleIds.Count == 0
            ? new List<long>()
            : await _db.RolePermissions
                .AsNoTracking()
                .Where(rp => roleIds.Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .ToListAsync(ct);

        var codes = permissionIds.Count == 0
            ? new List<string>()
            : await _db.Permissions
                .AsNoTracking()
                .Where(p => permissionIds.Contains(p.Id))
                .Select(p => p.Code)
                .ToListAsync(ct);

        if (!codes.Any(c => string.Equals(c, HospitalConfigPermissionCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Tài khoản không có quyền cấu hình danh mục bệnh viện (HOSPITAL_CONFIG).");
        }

        return access;
    }

    private async Task<DiagnosticServiceAdminItemDto> LoadItemAsync(
        long hospitalId,
        long serviceId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var service = await _db.Services
            .AsNoTracking()
            .Include(s => s.Department)
            .Include(s => s.Specialty)
            .Include(s => s.ServicePrices)
            .FirstAsync(
                s => s.Id == serviceId
                     && s.HospitalId == hospitalId
                     && s.ServiceType.ToUpper() == DiagnosticServiceType,
                ct);

        var diagPoints = await _db.QueueServicePoints
            .AsNoTracking()
            .Where(sp => sp.PointType != null
                         && sp.PointType.ToUpper() == DiagnosticServiceType
                         && sp.ServiceId == serviceId
                         && (sp.HospitalId == hospitalId || sp.HospitalId == null))
            .ToListAsync(ct);

        var roomIds = diagPoints
            .Where(sp => sp.RoomId.HasValue)
            .Select(sp => sp.RoomId!.Value)
            .Distinct()
            .ToList();

        var roomsById = roomIds.Count == 0
            ? new Dictionary<long, HospitalRoom>()
            : await _db.HospitalRooms
                .AsNoTracking()
                .Where(r => roomIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, ct);

        return MapItem(service, diagPoints, roomsById, DateTime.SpecifyKind(nowUtc, DateTimeKind.Unspecified));
    }

    private async Task<Service> FindScopedServiceForUpdateAsync(
        long hospitalId,
        long serviceId,
        CancellationToken ct)
    {
        return await _db.Services.FirstOrDefaultAsync(
                s => s.Id == serviceId
                     && s.HospitalId == hospitalId
                     && s.ServiceType.ToUpper() == DiagnosticServiceType,
                ct)
            ?? throw new NotFoundException("Service", serviceId);
    }

    private async Task EnsureReferencesAsync(
        long hospitalId,
        long departmentId,
        long? specialtyId,
        long? unchangedDepartmentId,
        long? unchangedSpecialtyId,
        CancellationToken ct)
    {
        var department = await _db.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == departmentId && d.HospitalId == hospitalId, ct)
            ?? throw new NotFoundException("Department", departmentId);

        if (department.IsActive == false && departmentId != unchangedDepartmentId)
        {
            throw new ConflictException("Khoa thực hiện đang ngừng hoạt động.");
        }

        if (specialtyId is null)
        {
            return;
        }

        var specialty = await _db.Specialties
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == specialtyId.Value, ct)
            ?? throw new NotFoundException("Specialty", specialtyId.Value);

        if (specialty.IsActive == false && specialtyId != unchangedSpecialtyId)
        {
            throw new ConflictException("Chuyên khoa đang ngừng hoạt động.");
        }
    }

    private readonly record struct NormalizedWriteFields(
        string? ServiceCode,
        string Name,
        string? Description,
        long DepartmentId,
        long? SpecialtyId,
        bool? RequiresResultVerification,
        decimal? Price);

    private static NormalizedWriteFields ValidateCreate(CreateDiagnosticServiceAdminRequest? request)
    {
        if (request is null)
        {
            throw new ValidationException("Dữ liệu dịch vụ cận lâm sàng không được để trống.");
        }

        var (name, description, departmentId, specialtyId) = ValidateCommon(
            request.Name,
            request.Description,
            request.DepartmentId,
            request.SpecialtyId);

        return new NormalizedWriteFields(
            ValidateServiceCode(request.ServiceCode),
            name,
            description,
            departmentId,
            specialtyId,
            request.RequiresResultVerification,
            ValidatePrice(request.Price));
    }

    private static NormalizedWriteFields ValidateUpdate(UpdateDiagnosticServiceAdminRequest? request)
    {
        if (request is null)
        {
            throw new ValidationException("Dữ liệu dịch vụ cận lâm sàng không được để trống.");
        }

        var (name, description, departmentId, specialtyId) = ValidateCommon(
            request.Name,
            request.Description,
            request.DepartmentId,
            request.SpecialtyId);

        return new NormalizedWriteFields(
            null,
            name,
            description,
            departmentId,
            specialtyId,
            request.RequiresResultVerification,
            null);
    }

    private static (string Name, string? Description, long DepartmentId, long? SpecialtyId) ValidateCommon(
        string? name,
        string? description,
        long? departmentId,
        long? specialtyId)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0)
        {
            throw new ValidationException("Tên dịch vụ là bắt buộc.");
        }

        if (normalizedName.Length > 200)
        {
            throw new ValidationException("Tên dịch vụ tối đa 200 ký tự.");
        }

        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (normalizedDescription is not null && normalizedDescription.Length > 2000)
        {
            throw new ValidationException("Mô tả dịch vụ tối đa 2000 ký tự.");
        }

        if (departmentId is null or <= 0)
        {
            throw new ValidationException("Khoa thực hiện là bắt buộc.");
        }

        if (specialtyId is <= 0)
        {
            throw new ValidationException("Chuyên khoa không hợp lệ.");
        }

        return (normalizedName, normalizedDescription, departmentId.Value, specialtyId);
    }

    private static string ValidateServiceCode(string? serviceCode)
    {
        var code = serviceCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code.Length == 0)
        {
            throw new ValidationException("Mã dịch vụ là bắt buộc.");
        }

        if (code.Length > 40)
        {
            throw new ValidationException("Mã dịch vụ tối đa 40 ký tự.");
        }

        if (!ServiceCodeRegex.IsMatch(code))
        {
            throw new ValidationException("Mã dịch vụ chỉ gồm chữ in hoa, số và các ký tự - _ .");
        }

        return code;
    }

    private static decimal? ValidatePrice(decimal? price)
    {
        if (price is null)
        {
            return null;
        }

        if (price <= 0 || price > MaxPrice)
        {
            throw new ValidationException("Đơn giá phải lớn hơn 0 và không quá 1.000.000.000 đ.");
        }

        if (decimal.Round(price.Value, 2) != price.Value)
        {
            throw new ValidationException("Đơn giá tối đa 2 chữ số thập phân.");
        }

        return price;
    }
}
