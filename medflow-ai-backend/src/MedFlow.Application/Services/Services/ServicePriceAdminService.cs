using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Services;

public sealed class ServicePriceAdminService : IServicePriceAdminService
{
    public const string ServicePriceUpdatedAction = "SERVICE_PRICE_UPDATED";

    private const string ServiceEntityType = "SERVICE";
    private const string UnknownFacilityFallback = "Chưa cập nhật";
    private const decimal MaxAllowedPrice = 1_000_000_000m;
    private const int MaxPageSize = 200;

    private static readonly CultureInfo ViCulture = new("vi-VN");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public ServicePriceAdminService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ServicePriceAdminListResponseDto> GetServicePriceListAsync(
        long actorUserId,
        GetServicePriceListAdminRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Page < 1)
        {
            throw new ValidationException("Trang yêu cầu phải lớn hơn hoặc bằng 1.");
        }

        if (request.PageSize < 1 || request.PageSize > MaxPageSize)
        {
            throw new ValidationException($"Số lượng bản ghi trên một trang phải từ 1 đến {MaxPageSize}.");
        }

        if (request.DepartmentId is <= 0)
        {
            throw new ValidationException("Mã khoa phòng lọc không hợp lệ.");
        }

        if (request.MinPrice is < 0)
        {
            throw new ValidationException("Mức giá tối thiểu lọc không được là số âm.");
        }

        if (request.MaxPrice is < 0)
        {
            throw new ValidationException("Mức giá tối đa lọc không được là số âm.");
        }

        if (request.MinPrice.HasValue && request.MaxPrice.HasValue && request.MinPrice.Value > request.MaxPrice.Value)
        {
            throw new ValidationException("Mức giá tối thiểu không được lớn hơn mức giá tối đa.");
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var (isSystemAdmin, actorHospitalId, _) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        long? scopedHospitalId = request.HospitalId;
        string? scopedHospitalName = null;

        if (!isSystemAdmin)
        {
            if (request.HospitalId.HasValue && request.HospitalId.Value != actorHospitalId!.Value)
            {
                throw new ForbiddenException("Bạn không có quyền xem bảng giá dịch vụ thuộc cơ sở y tế khác.");
            }

            scopedHospitalId = actorHospitalId!.Value;
        }
        else if (request.HospitalId.HasValue)
        {
            if (request.HospitalId.Value <= 0)
            {
                throw new ValidationException("Mã cơ sở y tế không hợp lệ.");
            }

            var requestedHospital = await _db.Hospitals
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == request.HospitalId.Value, ct)
                ?? throw new NotFoundException("Hospital", request.HospitalId.Value);

            scopedHospitalName = requestedHospital.Name;
        }

        var baseQuery = _db.Services
            .AsNoTracking()
            .Include(s => s.Department)
            .Include(s => s.ServicePrices)
            .AsQueryable();

        if (scopedHospitalId.HasValue)
        {
            baseQuery = baseQuery.Where(s => s.HospitalId == scopedHospitalId.Value);
        }

        var allScopedServices = await baseQuery
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        var hospitalIds = allScopedServices
            .Where(s => s.HospitalId.HasValue)
            .Select(s => s.HospitalId!.Value)
            .Distinct()
            .ToList();

        if (scopedHospitalId.HasValue && !hospitalIds.Contains(scopedHospitalId.Value))
        {
            hospitalIds.Add(scopedHospitalId.Value);
        }

        var hospitalsById = hospitalIds.Count == 0
            ? new Dictionary<long, string>()
            : await _db.Hospitals
                .AsNoTracking()
                .Where(h => hospitalIds.Contains(h.Id))
                .ToDictionaryAsync(h => h.Id, h => h.Name, ct);

        if (scopedHospitalId.HasValue && scopedHospitalName is null &&
            hospitalsById.TryGetValue(scopedHospitalId.Value, out var resolvedHospName))
        {
            scopedHospitalName = resolvedHospName;
        }

        var allServiceIds = allScopedServices.Select(s => s.Id).ToList();
        var auditMetaByPriceId = await LoadPriceAuditMetadataAsync(allServiceIds, ct);

        var allMappedItems = allScopedServices
            .Select(s => MapServicePriceItem(s, hospitalsById, auditMetaByPriceId, nowLocal))
            .ToList();

        var metrics = BuildMetrics(allMappedItems);

        IEnumerable<ServicePriceAdminItemDto> filtered = allMappedItems;

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim();
            filtered = filtered.Where(item =>
                item.ServiceCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                item.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                (item.DepartmentName != null && item.DepartmentName.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                (item.Description != null && item.Description.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        }

        if (request.DepartmentId.HasValue)
        {
            filtered = filtered.Where(item => item.DepartmentId == request.DepartmentId.Value);
        }

        var rawTypeOrCategory = !string.IsNullOrWhiteSpace(request.Category)
            ? request.Category
            : request.ServiceType;

        if (!string.IsNullOrWhiteSpace(rawTypeOrCategory))
        {
            var normalizedType = NormalizeServiceTypeFilter(rawTypeOrCategory);
            filtered = filtered.Where(item =>
                string.Equals(item.ServiceType, normalizedType, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Category, rawTypeOrCategory.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (request.IsActive.HasValue)
        {
            filtered = filtered.Where(item => item.IsActive == request.IsActive.Value);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var st = request.Status.Trim().ToUpperInvariant();
            if (st == "ACTIVE")
            {
                filtered = filtered.Where(item => item.IsActive);
            }
            else if (st is "SUSPENDED" or "INACTIVE")
            {
                filtered = filtered.Where(item => !item.IsActive);
            }
            else if (st != "ALL")
            {
                throw new ValidationException("Trạng thái dịch vụ lọc không hợp lệ.");
            }
        }

        if (request.MinPrice.HasValue)
        {
            filtered = filtered.Where(item => item.Price >= request.MinPrice.Value);
        }

        if (request.MaxPrice.HasValue)
        {
            filtered = filtered.Where(item => item.Price <= request.MaxPrice.Value);
        }

        var filteredList = filtered.ToList();
        var totalFiltered = filteredList.Count;
        var totalPages = totalFiltered == 0
            ? 1
            : (int)Math.Ceiling((double)totalFiltered / request.PageSize);

        var pageItems = filteredList
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new ServicePriceAdminListResponseDto(
            HospitalId: scopedHospitalId,
            HospitalName: scopedHospitalName,
            Data: pageItems,
            Total: totalFiltered,
            Page: request.Page,
            PageSize: request.PageSize,
            TotalPages: totalPages,
            Metrics: metrics);
    }

    public async Task<ServicePriceDetailResponseDto> GetServicePriceDetailAsync(
        long actorUserId,
        string idOrCode,
        long? hospitalId = null,
        CancellationToken ct = default)
    {
        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var (isSystemAdmin, actorHospitalId, _) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        if (!isSystemAdmin && hospitalId.HasValue && hospitalId.Value != actorHospitalId!.Value)
        {
            throw new ForbiddenException("Bạn không có quyền xem lịch sử giá dịch vụ thuộc cơ sở y tế khác.");
        }

        var service = await ResolveScopedServiceAsync(
            idOrCode,
            isSystemAdmin,
            actorHospitalId,
            hospitalId,
            asNoTracking: true,
            ct);

        var hospitalsById = new Dictionary<long, string>();
        if (service.HospitalId.HasValue)
        {
            var hospital = await _db.Hospitals
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == service.HospitalId.Value, ct);
            if (hospital is not null)
            {
                hospitalsById[hospital.Id] = hospital.Name;
            }
        }

        var auditMetaByPriceId = await LoadPriceAuditMetadataAsync([service.Id], ct);
        var mapped = MapServicePriceItem(service, hospitalsById, auditMetaByPriceId, nowLocal);

        return new ServicePriceDetailResponseDto(
            ServiceId: mapped.Id,
            ServiceCode: mapped.ServiceCode,
            Name: mapped.Name,
            ServiceType: mapped.ServiceType,
            Category: mapped.Category,
            HospitalId: mapped.HospitalId,
            Facility: mapped.Facility,
            DepartmentId: mapped.DepartmentId,
            DepartmentName: mapped.DepartmentName,
            IsActive: mapped.IsActive,
            Status: mapped.Status,
            Price: mapped.Price,
            CurrentPriceAmount: mapped.CurrentPriceAmount,
            HasEffectivePrice: mapped.HasEffectivePrice,
            PriceFormatted: mapped.PriceFormatted,
            ValidFrom: mapped.ValidFrom,
            ValidTo: mapped.ValidTo,
            EffectiveDate: mapped.EffectiveDate,
            DecisionNumber: mapped.DecisionNumber,
            AppliedBy: mapped.AppliedBy,
            Note: mapped.Note,
            PriceHistory: mapped.PriceHistory);
    }

    public async Task<ServicePriceAdminItemDto> UpdateServicePriceAsync(
        long actorUserId,
        string? routeIdOrCode,
        UpdateServicePriceAdminRequest? request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (request is null)
        {
            throw new ValidationException("Dữ liệu cập nhật giá dịch vụ không được để trống.");
        }

        var targetIdentifier = !string.IsNullOrWhiteSpace(routeIdOrCode)
            ? routeIdOrCode.Trim()
            : request.ServiceId.HasValue
                ? request.ServiceId.Value.ToString(CultureInfo.InvariantCulture)
                : request.ServiceCode?.Trim();

        if (string.IsNullOrWhiteSpace(targetIdentifier))
        {
            throw new ValidationException("Vui lòng chỉ định mã hoặc định danh dịch vụ cần cập nhật giá.");
        }

        var rawPrice = request.PriceAmount ?? request.Price;
        if (!rawPrice.HasValue)
        {
            throw new ValidationException("Vui lòng nhập mức giá dịch vụ hợp lệ (PriceAmount >= 0).");
        }

        var priceAmount = rawPrice.Value;
        if (priceAmount < 0m)
        {
            throw new ValidationException("Đơn giá dịch vụ phải lớn hơn hoặc bằng 0 (PriceAmount >= 0).");
        }

        if (priceAmount > MaxAllowedPrice)
        {
            throw new ValidationException("Đơn giá dịch vụ không được vượt quá 1.000.000.000 đ.");
        }

        if (decimal.Round(priceAmount, 2) != priceAmount)
        {
            throw new ValidationException("Đơn giá dịch vụ tối đa 2 chữ số thập phân.");
        }

        var decisionNumber = NormalizeOptionalText(request.DecisionNumber, 100, "Số quyết định giá");
        var appliedByInput = NormalizeOptionalText(request.AppliedBy, 150, "Người/cấp phê duyệt");
        var noteInput = NormalizeOptionalText(request.Note ?? request.Reason, 500, "Ghi chú / lý do điều chỉnh giá");

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var preLockNowLocal = HospitalClock.GetNow(_timeProvider);
        var preLockTodayLocal = HospitalClock.GetToday(_timeProvider);

        var rawValidFrom = !string.IsNullOrWhiteSpace(request.ValidFrom)
            ? request.ValidFrom
            : request.EffectiveDate;

        // Pre-validate date syntax & ordering before acquiring DB locks.
        var (_, preLockStartDateOnly) = ParseValidFrom(rawValidFrom, preLockNowLocal, preLockTodayLocal);
        _ = ParseValidTo(request.ValidTo, preLockStartDateOnly, preLockStartDateOnly?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified) ?? preLockNowLocal);

        var (isSystemAdmin, actorHospitalId, actorDisplayName) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        if (!isSystemAdmin && request.HospitalId.HasValue && request.HospitalId.Value != actorHospitalId!.Value)
        {
            throw new ForbiddenException("Bạn không có quyền cập nhật giá dịch vụ thuộc cơ sở y tế khác.");
        }

        var initialService = await ResolveScopedServiceAsync(
            targetIdentifier,
            isSystemAdmin,
            actorHospitalId,
            request.HospitalId,
            asNoTracking: true,
            ct);

        if (!initialService.HospitalId.HasValue || initialService.HospitalId.Value <= 0)
        {
            throw new ValidationException("Dịch vụ chưa được gắn cơ sở y tế hợp lệ.");
        }

        var hospital = await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == initialService.HospitalId.Value, ct)
            ?? throw new NotFoundException("Hospital", initialService.HospitalId.Value);

        await using var tx = await AppointmentBookingService.BeginTransactionSafeAsync(_db, ct);

        Service service;
        DateTime nowLocal;

        try
        {
            // ADR-0059 Level 04: lock parent service row FOR UPDATE to serialize all ServicePrice intervals.
            await _db.TryLockServiceForUpdateAsync(initialService.Id, ct);

            if (AppointmentBookingService.IsRelationalDatabase(_db) && _db is DbContext efDb)
            {
                efDb.ChangeTracker.Clear();
            }

            service = await _db.Services
                .Include(s => s.Department)
                .Include(s => s.ServicePrices)
                .FirstOrDefaultAsync(s => s.Id == initialService.Id, ct)
                ?? throw new NotFoundException("Service", initialService.Id);

            nowUtc = HospitalClock.GetUtcNow(_timeProvider);
            nowLocal = HospitalClock.GetNow(_timeProvider);
            var todayLocal = HospitalClock.GetToday(_timeProvider);

            var (effectiveFrom, startDateOnly) = ParseValidFrom(rawValidFrom, nowLocal, todayLocal);
            var effectiveTo = ParseValidTo(request.ValidTo, startDateOnly, effectiveFrom);

            var previousActivePrice = ResolveActivePriceRecord(service.ServicePrices, nowLocal);
            var closedPriceIds = new List<long>();
            var resumedPricesToInsert = new List<ServicePrice>();

            var validExistingPrices = service.ServicePrices
                .Where(p => !p.EffectiveFrom.HasValue || !p.EffectiveTo.HasValue || p.EffectiveTo.Value > p.EffectiveFrom.Value)
                .OrderBy(p => p.EffectiveFrom ?? DateTime.MinValue)
                .ThenBy(p => p.Id)
                .ToList();

            var overlappingPrices = validExistingPrices
                .Where(p => IntervalsOverlap(
                    p.EffectiveFrom ?? DateTime.MinValue,
                    p.EffectiveTo,
                    effectiveFrom,
                    effectiveTo))
                .ToList();

            foreach (var existing in overlappingPrices)
            {
                var existingStart = existing.EffectiveFrom ?? DateTime.MinValue;
                var existingEnd = existing.EffectiveTo;

                // Reject any overlapping scheduled/future window starting at or after effectiveFrom,
                // or any bounded future window already scheduled ahead of nowLocal.
                if (existingStart >= effectiveFrom ||
                    (existingStart > nowLocal && existingEnd.HasValue))
                {
                    throw new ConflictException(
                        "Khoảng thời gian hiệu lực bị chồng lấn với biểu phí đã được lên lịch trước đó. Vui lòng chọn khoảng hiệu lực không chồng lấn.");
                }

                if (effectiveTo.HasValue)
                {
                    // Bounded promotional/temporary window [effectiveFrom, effectiveTo):
                    // existing baseline must cover the window so it can be cleanly split and resumed.
                    if (existingEnd.HasValue && existingEnd.Value < effectiveTo.Value)
                    {
                        throw new ConflictException(
                            "Khoảng thời gian hiệu lực khuyến mãi/tạm thời vượt quá mốc kết thúc của biểu phí hiện hành.");
                    }

                    var originalEnd = existingEnd;
                    existing.EffectiveTo = effectiveFrom;
                    closedPriceIds.Add(existing.Id);

                    if (!originalEnd.HasValue || originalEnd.Value > effectiveTo.Value)
                    {
                        resumedPricesToInsert.Add(new ServicePrice
                        {
                            ServiceId = service.Id,
                            PriceAmount = existing.PriceAmount,
                            EffectiveFrom = effectiveTo.Value,
                            EffectiveTo = originalEnd,
                        });
                    }
                }
                else
                {
                    // Open-ended replacement [effectiveFrom, +inf): close prior baseline cleanly at effectiveFrom.
                    existing.EffectiveTo = effectiveFrom;
                    closedPriceIds.Add(existing.Id);
                }
            }

            var newPrice = new ServicePrice
            {
                ServiceId = service.Id,
                PriceAmount = priceAmount,
                EffectiveFrom = effectiveFrom,
                EffectiveTo = effectiveTo,
            };

            service.ServicePrices.Add(newPrice);
            _db.ServicePrices.Add(newPrice);

            foreach (var resumed in resumedPricesToInsert)
            {
                service.ServicePrices.Add(resumed);
                _db.ServicePrices.Add(resumed);
            }

            await _db.SaveChangesAsync(ct);

            var resolvedAppliedBy = appliedByInput ?? actorDisplayName;
            var resolvedReason = noteInput
                ?? (!string.IsNullOrWhiteSpace(decisionNumber)
                    ? $"Cập nhật biểu phí dịch vụ theo quyết định {decisionNumber}"
                    : $"Cập nhật biểu phí dịch vụ {service.ServiceCode}");

            var parsedIp = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var ip)
                ? ip
                : null;

            var auditPayload = new PriceAuditPayload(
                ServicePriceId: newPrice.Id,
                ServiceId: service.Id,
                ServiceCode: service.ServiceCode,
                ServiceName: service.Name,
                PreviousPriceAmount: previousActivePrice?.PriceAmount,
                PriceAmount: priceAmount,
                ClosedPriceIds: closedPriceIds,
                ValidFrom: effectiveFrom,
                ValidTo: effectiveTo,
                DecisionNumber: decisionNumber,
                AppliedBy: resolvedAppliedBy,
                Note: noteInput ?? resolvedReason);

            _db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                HospitalId = service.HospitalId,
                Action = ServicePriceUpdatedAction,
                EntityType = ServiceEntityType,
                EntityId = service.Id.ToString(CultureInfo.InvariantCulture),
                Reason = resolvedReason,
                ChangeData = JsonSerializer.Serialize(auditPayload, JsonOptions),
                IpAddress = parsedIp,
                OccurredAt = nowUtc,
            });

            await _db.SaveChangesAsync(ct);

            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }
        }
        catch (Exception ex) when (AppointmentBookingService.IsConcurrencyOrLockConflict(ex))
        {
            await AppointmentBookingService.RollbackAndClearTrackerAsync(_db, tx, ct);
            throw new ConflictException("Biểu phí dịch vụ đang được cập nhật đồng thời bởi quản trị viên khác. Vui lòng tải lại và thử lại.");
        }

        var hospitalsById = new Dictionary<long, string>
        {
            [hospital.Id] = hospital.Name,
        };
        var auditMetaByPriceId = await LoadPriceAuditMetadataAsync([service.Id], ct);

        return MapServicePriceItem(service, hospitalsById, auditMetaByPriceId, nowLocal);
    }

    private async Task<Service> ResolveScopedServiceAsync(
        string idOrCode,
        bool isSystemAdmin,
        long? actorHospitalId,
        long? requestedHospitalId,
        bool asNoTracking,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã hoặc định danh dịch vụ không được để trống.");
        }

        var trimmed = idOrCode.Trim();
        if (trimmed.Length > 80)
        {
            throw new ValidationException("Mã hoặc định danh dịch vụ không được vượt quá 80 ký tự.");
        }

        if (isSystemAdmin && requestedHospitalId is <= 0)
        {
            throw new ValidationException("Mã cơ sở y tế không hợp lệ.");
        }

        var query = asNoTracking
            ? _db.Services.AsNoTracking()
            : _db.Services.AsQueryable();

        query = query
            .Include(s => s.Department)
            .Include(s => s.ServicePrices);

        var isNumeric = long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId) && parsedId > 0;

        Service? service;
        if (isNumeric)
        {
            service = await query.FirstOrDefaultAsync(s => s.Id == parsedId, ct);
            if (service is null)
            {
                var upper = trimmed.ToUpperInvariant();
                service = await query.FirstOrDefaultAsync(s => s.ServiceCode.ToUpper() == upper, ct);
            }
        }
        else
        {
            var upper = trimmed.ToUpperInvariant();
            var targetHospId = !isSystemAdmin ? actorHospitalId : requestedHospitalId;

            if (targetHospId.HasValue)
            {
                service = await query.FirstOrDefaultAsync(
                    s => s.HospitalId == targetHospId.Value && s.ServiceCode.ToUpper() == upper,
                    ct);

                if (service is null && !isSystemAdmin)
                {
                    var existsInOtherHospital = await _db.Services
                        .AsNoTracking()
                        .AnyAsync(s => s.ServiceCode.ToUpper() == upper && s.HospitalId != targetHospId.Value, ct);
                    if (existsInOtherHospital)
                    {
                        throw new ForbiddenException("Bạn không có quyền thao tác trên dịch vụ thuộc cơ sở y tế khác.");
                    }
                }
            }
            else
            {
                service = await query
                    .OrderBy(s => s.Id)
                    .FirstOrDefaultAsync(s => s.ServiceCode.ToUpper() == upper, ct);
            }
        }

        if (service is null)
        {
            throw new NotFoundException("Service", trimmed);
        }

        if (!isSystemAdmin && (!service.HospitalId.HasValue || service.HospitalId.Value != actorHospitalId!.Value))
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên dịch vụ thuộc cơ sở y tế khác.");
        }

        if (isSystemAdmin && requestedHospitalId.HasValue && service.HospitalId != requestedHospitalId.Value)
        {
            throw new NotFoundException("Service", trimmed);
        }

        return service;
    }

    private async Task<Dictionary<long, PriceAuditMetadata>> LoadPriceAuditMetadataAsync(
        IReadOnlyCollection<long> serviceIds,
        CancellationToken ct)
    {
        var result = new Dictionary<long, PriceAuditMetadata>();
        if (serviceIds.Count == 0)
        {
            return result;
        }

        var entityIdStrings = serviceIds
            .Select(id => id.ToString(CultureInfo.InvariantCulture))
            .ToList();

        var logs = await _db.AuditLogs
            .AsNoTracking()
            .Where(a =>
                a.Action == ServicePriceUpdatedAction &&
                a.EntityId != null &&
                entityIdStrings.Contains(a.EntityId))
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .ToListAsync(ct);

        foreach (var log in logs)
        {
            if (string.IsNullOrWhiteSpace(log.ChangeData))
            {
                continue;
            }

            try
            {
                var payload = JsonSerializer.Deserialize<PriceAuditPayload>(log.ChangeData, JsonOptions);
                if (payload is null || payload.ServicePriceId <= 0 || result.ContainsKey(payload.ServicePriceId))
                {
                    continue;
                }

                result[payload.ServicePriceId] = new PriceAuditMetadata(
                    DecisionNumber: string.IsNullOrWhiteSpace(payload.DecisionNumber) ? null : payload.DecisionNumber.Trim(),
                    AppliedBy: string.IsNullOrWhiteSpace(payload.AppliedBy) ? null : payload.AppliedBy.Trim(),
                    Note: !string.IsNullOrWhiteSpace(payload.Note)
                        ? payload.Note.Trim()
                        : (!string.IsNullOrWhiteSpace(log.Reason) ? log.Reason.Trim() : null));
            }
            catch (JsonException)
            {
                // Ignore malformed historical JSON entries cleanly
            }
        }

        return result;
    }

    private static ServicePriceAdminItemDto MapServicePriceItem(
        Service service,
        IReadOnlyDictionary<long, string> hospitalsById,
        IReadOnlyDictionary<long, PriceAuditMetadata> auditMetaByPriceId,
        DateTime nowLocal)
    {
        var isActive = service.IsActive ?? true;
        var status = isActive ? "ACTIVE" : "SUSPENDED";
        var allowsOnline = service.AllowsOnlineBooking ?? false;
        var category = ResolveCategory(service.ServiceType);
        var facility = service.HospitalId.HasValue &&
                       hospitalsById.TryGetValue(service.HospitalId.Value, out var hospitalName) &&
                       !string.IsNullOrWhiteSpace(hospitalName)
            ? hospitalName.Trim()
            : UnknownFacilityFallback;

        var orderedPrices = service.ServicePrices
            .Where(p => p.PriceAmount.HasValue && p.PriceAmount.Value >= 0m)
            .Where(p => !p.EffectiveFrom.HasValue || !p.EffectiveTo.HasValue || p.EffectiveTo.Value > p.EffectiveFrom.Value)
            .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
            .ThenByDescending(p => p.Id)
            .ToList();

        var activeRecord = ResolveActivePriceRecord(orderedPrices, nowLocal);
        var displayRecord = activeRecord ?? orderedPrices.FirstOrDefault();

        var historyDtos = orderedPrices.Select(p =>
        {
            var amount = p.PriceAmount!.Value;
            var formatted = FormatVnd(amount);
            var isRowActive = activeRecord is not null && activeRecord.Id == p.Id;
            var rowStatus = isRowActive
                ? "ACTIVE"
                : (p.EffectiveFrom.HasValue && p.EffectiveFrom.Value > nowLocal ? "SCHEDULED" : "EXPIRED");

            auditMetaByPriceId.TryGetValue(p.Id, out var meta);

            var dateText = p.EffectiveFrom.HasValue
                ? p.EffectiveFrom.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : UnknownFacilityFallback;

            return new ServicePriceHistoryEntryDto(
                Id: p.Id,
                Price: amount,
                PriceAmount: amount,
                PriceFormatted: formatted,
                ValidFrom: p.EffectiveFrom,
                ValidTo: p.EffectiveTo,
                Date: dateText,
                DecisionNumber: meta?.DecisionNumber,
                Note: meta?.Note,
                AppliedBy: meta?.AppliedBy,
                IsActive: isRowActive,
                Status: rowStatus);
        }).ToList();

        var currentPriceAmount = activeRecord?.PriceAmount;
        var hasEffectivePrice = currentPriceAmount.HasValue;
        var resolvedPrice = currentPriceAmount ?? displayRecord?.PriceAmount ?? 0m;
        var priceFormatted = hasEffectivePrice || displayRecord?.PriceAmount is not null
            ? FormatVnd(resolvedPrice)
            : UnknownFacilityFallback;

        PriceAuditMetadata? displayMeta = null;
        if (displayRecord is not null)
        {
            auditMetaByPriceId.TryGetValue(displayRecord.Id, out displayMeta);
        }

        var effectiveDate = displayRecord?.EffectiveFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new ServicePriceAdminItemDto(
            Id: service.Id,
            ServiceCode: service.ServiceCode,
            Name: service.Name,
            Description: service.Description,
            ServiceType: service.ServiceType,
            Category: category,
            HospitalId: service.HospitalId,
            Facility: facility,
            DepartmentId: service.DepartmentId,
            DepartmentCode: service.Department?.DepartmentCode,
            DepartmentName: service.Department?.Name,
            Price: resolvedPrice,
            CurrentPriceAmount: currentPriceAmount,
            HasEffectivePrice: hasEffectivePrice,
            PriceFormatted: priceFormatted,
            AllowsOnlineBooking: allowsOnline,
            IsActive: isActive,
            Status: status,
            ValidFrom: displayRecord?.EffectiveFrom,
            ValidTo: displayRecord?.EffectiveTo,
            EffectiveDate: effectiveDate,
            DecisionNumber: displayMeta?.DecisionNumber,
            AppliedBy: displayMeta?.AppliedBy,
            Note: displayMeta?.Note,
            PriceHistory: historyDtos);
    }

    private static ServicePrice? ResolveActivePriceRecord(
        IEnumerable<ServicePrice> prices,
        DateTime nowLocal)
    {
        return prices
            .Where(p => p.PriceAmount.HasValue && p.PriceAmount.Value >= 0m)
            .Where(p => !p.EffectiveFrom.HasValue || p.EffectiveFrom.Value <= nowLocal)
            .Where(p => !p.EffectiveTo.HasValue || p.EffectiveTo.Value > nowLocal)
            .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
            .ThenByDescending(p => p.Id)
            .FirstOrDefault();
    }

    private static ServicePricingMetricsDto BuildMetrics(IReadOnlyList<ServicePriceAdminItemDto> items)
    {
        if (items.Count == 0)
        {
            return new ServicePricingMetricsDto(
                TotalServices: 0,
                PricedServicesCount: 0,
                PricedRatioPercent: 0,
                AveragePrice: 0m,
                MinPrice: 0m,
                MaxPrice: 0m,
                LatestDecisionNumber: null,
                LatestEffectiveDate: null);
        }

        var pricedCount = items.Count(i => i.HasEffectivePrice);
        var ratio = (int)Math.Round((double)pricedCount * 100.0 / items.Count);
        var prices = items.Select(i => i.Price).ToList();
        var avg = Math.Round(prices.Average(), 0, MidpointRounding.AwayFromZero);
        var min = prices.Min();
        var max = prices.Max();

        var latestWithDecision = items
            .Where(i => !string.IsNullOrWhiteSpace(i.DecisionNumber))
            .OrderByDescending(i => i.ValidFrom ?? DateTime.MinValue)
            .FirstOrDefault();

        var latestAny = latestWithDecision
            ?? items
                .Where(i => i.ValidFrom.HasValue)
                .OrderByDescending(i => i.ValidFrom!.Value)
                .FirstOrDefault();

        return new ServicePricingMetricsDto(
            TotalServices: items.Count,
            PricedServicesCount: pricedCount,
            PricedRatioPercent: ratio,
            AveragePrice: avg,
            MinPrice: min,
            MaxPrice: max,
            LatestDecisionNumber: latestWithDecision?.DecisionNumber,
            LatestEffectiveDate: latestAny?.EffectiveDate);
    }

    private static bool IntervalsOverlap(
        DateTime startA,
        DateTime? endA,
        DateTime startB,
        DateTime? endB)
    {
        var aBeforeBEnd = !endB.HasValue || startA < endB.Value;
        var bBeforeAEnd = !endA.HasValue || startB < endA.Value;
        return aBeforeBEnd && bBeforeAEnd;
    }

    private static DateTime ParseHospitalTimestamp(string trimmed, string errorMessage)
    {
        if (trimmed.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
            (trimmed.Length > 10 && (trimmed[10..].Contains('+') || trimmed[10..].Contains('-'))))
        {
            if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            {
                var converted = TimeZoneInfo.ConvertTime(dto, HospitalClock.TimeZone);
                return DateTime.SpecifyKind(converted.DateTime, DateTimeKind.Unspecified);
            }
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDt))
        {
            return DateTime.SpecifyKind(parsedDt, DateTimeKind.Unspecified);
        }

        throw new ValidationException(errorMessage);
    }

    private static (DateTime EffectiveFrom, DateOnly? StartDateOnly) ParseValidFrom(
        string? rawValidFrom,
        DateTime nowLocal,
        DateOnly todayLocal)
    {
        if (string.IsNullOrWhiteSpace(rawValidFrom))
        {
            return (nowLocal, todayLocal);
        }

        var trimmed = rawValidFrom.Trim();
        if (DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            if (dateOnly == todayLocal)
            {
                return (nowLocal, dateOnly);
            }

            return (dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), dateOnly);
        }

        var localDt = ParseHospitalTimestamp(
            trimmed,
            "Ngày bắt đầu hiệu lực (ValidFrom) không hợp lệ. Vui lòng dùng định dạng YYYY-MM-DD.");
        return (localDt, null);
    }

    private static DateTime? ParseValidTo(
        string? rawValidTo,
        DateOnly? startDateOnly,
        DateTime effectiveFrom)
    {
        if (string.IsNullOrWhiteSpace(rawValidTo))
        {
            return null;
        }

        var trimmed = rawValidTo.Trim();
        DateTime effectiveTo;

        if (DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDateOnly))
        {
            if (startDateOnly.HasValue && endDateOnly < startDateOnly.Value)
            {
                throw new ValidationException("Ngày bắt đầu hiệu lực (ValidFrom) phải nhỏ hơn hoặc bằng ngày kết thúc hiệu lực (ValidTo).");
            }

            // Half-open [EffectiveFrom, EffectiveTo): YYYY-MM-DD ValidTo is inclusive of that calendar day in UTC+7,
            // so its exclusive upper bound is 00:00:00 of the next hospital calendar day.
            effectiveTo = endDateOnly.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        }
        else
        {
            effectiveTo = ParseHospitalTimestamp(
                trimmed,
                "Ngày kết thúc hiệu lực (ValidTo) không hợp lệ. Vui lòng dùng định dạng YYYY-MM-DD.");
        }

        if (effectiveFrom >= effectiveTo)
        {
            throw new ValidationException("Thời điểm bắt đầu hiệu lực (ValidFrom) phải nhỏ hơn thời điểm kết thúc hiệu lực (ValidTo).");
        }

        return effectiveTo;
    }

    private static string? NormalizeOptionalText(string? raw, int maxLength, string fieldLabel)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ValidationException($"{fieldLabel} không được vượt quá {maxLength} ký tự.");
        }

        return trimmed;
    }

    private static string NormalizeServiceTypeFilter(string rawFilter)
    {
        var lower = rawFilter.Trim().ToLowerInvariant();
        return lower switch
        {
            "khám lâm sàng" or "examination" => "EXAMINATION",
            "hội chẩn lâm sàng" or "consultation" => "CONSULTATION",
            "cận lâm sàng kỹ thuật" or "diagnostic" => "DIAGNOSTIC",
            _ => rawFilter.Trim().ToUpperInvariant(),
        };
    }

    private static string ResolveCategory(string? serviceType)
    {
        if (string.Equals(serviceType, "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
        {
            return "Cận lâm sàng kỹ thuật";
        }

        if (string.Equals(serviceType, "CONSULTATION", StringComparison.OrdinalIgnoreCase))
        {
            return "Hội chẩn Lâm sàng";
        }

        return "Khám Lâm sàng";
    }

    private static string FormatVnd(decimal amount)
        => amount.ToString("N0", ViCulture) + " đ";

    private async Task<(bool IsSystemAdmin, long? ActorHospitalId, string ActorDisplayName)> EnsureAdminAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var actorUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

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
            .Where(ur => ur.UserId == actorUserId)
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
            throw new ForbiddenException("Người dùng không có quyền quản trị biểu phí dịch vụ.");
        }

        var hasHospitalConfigPerm = actorRoleIds.Count > 0 && await (
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
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is not null &&
            !string.IsNullOrWhiteSpace(actorStaff.Status) &&
            !string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của quản trị viên không ở trạng thái hoạt động.");
        }

        var displayName = !string.IsNullOrWhiteSpace(actorStaff?.FullName)
            ? actorStaff.FullName.Trim()
            : actorUser.Email;

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

            return (false, actorStaff.HospitalId.Value, displayName);
        }

        return (true, actorStaff?.HospitalId, displayName);
    }

    private sealed record PriceAuditMetadata(
        string? DecisionNumber,
        string? AppliedBy,
        string? Note);

    private sealed record PriceAuditPayload(
        long ServicePriceId,
        long ServiceId,
        string ServiceCode,
        string ServiceName,
        decimal? PreviousPriceAmount,
        decimal PriceAmount,
        IReadOnlyList<long> ClosedPriceIds,
        DateTime ValidFrom,
        DateTime? ValidTo,
        string? DecisionNumber,
        string? AppliedBy,
        string? Note);
}
