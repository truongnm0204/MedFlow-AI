using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Pharmacy;

/// <summary>
/// Admin CRUD + effective-history for medicine prices (#865). Replicates
/// <c>ServicePriceAdminService</c> semantics (route/idOrCode resolution, hospital scoping,
/// half-open effective windows, split/resume on overlap, audit log) with medicine scope.
/// </summary>
public sealed class MedicinePriceAdminService : IMedicinePriceAdminService
{
    public const string MedicinePriceUpdatedAction = "MEDICINE_PRICE_UPDATED";

    private const string MedicineEntityType = "MEDICINE";
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

    public MedicinePriceAdminService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<MedicinePriceAdminListResponseDto> GetMedicinePriceListAsync(
        long actorUserId,
        GetMedicinePriceListAdminRequest request,
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

        if (request.MedicineId is <= 0)
        {
            throw new ValidationException("Mã thuốc lọc không hợp lệ.");
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
                throw new ForbiddenException("Bạn không có quyền xem bảng giá thuốc thuộc cơ sở y tế khác.");
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

        var atLocal = ParseEffectiveAt(request.EffectiveAt, nowLocal);

        var baseQuery = _db.Medicines
            .AsNoTracking()
            .Include(m => m.MedicinePrices)
            .AsQueryable();

        if (scopedHospitalId.HasValue)
        {
            baseQuery = baseQuery.Where(m => m.HospitalId == scopedHospitalId.Value);
        }

        var allScopedMedicines = await baseQuery
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        var hospitalIds = allScopedMedicines
            .Where(m => m.HospitalId.HasValue)
            .Select(m => m.HospitalId!.Value)
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

        var allMappedItems = allScopedMedicines
            .Select(m => MapMedicinePriceItem(m, hospitalsById, atLocal))
            .ToList();

        var metrics = BuildMetrics(allMappedItems);

        IEnumerable<MedicinePriceAdminItemDto> filtered = allMappedItems;

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim();
            filtered = filtered.Where(item =>
                (item.MedicineCode != null && item.MedicineCode.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                item.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                (item.ActiveIngredient != null && item.ActiveIngredient.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        }

        if (request.MedicineId.HasValue)
        {
            filtered = filtered.Where(item => item.Id == request.MedicineId.Value);
        }

        if (request.IsActive.HasValue)
        {
            filtered = filtered.Where(item => item.IsActive == request.IsActive.Value);
        }

        if (request.HasEffectivePrice.HasValue)
        {
            filtered = filtered.Where(item => item.HasEffectivePrice == request.HasEffectivePrice.Value);
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

        return new MedicinePriceAdminListResponseDto(
            HospitalId: scopedHospitalId,
            HospitalName: scopedHospitalName,
            Data: pageItems,
            Total: totalFiltered,
            Page: request.Page,
            PageSize: request.PageSize,
            TotalPages: totalPages,
            Metrics: metrics);
    }

    public async Task<MedicinePriceDetailResponseDto> GetMedicinePriceDetailAsync(
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
            throw new ForbiddenException("Bạn không có quyền xem lịch sử giá thuốc thuộc cơ sở y tế khác.");
        }

        var medicine = await ResolveScopedMedicineAsync(
            idOrCode,
            isSystemAdmin,
            actorHospitalId,
            hospitalId,
            asNoTracking: true,
            ct);

        var hospitalsById = await LoadHospitalNamesAsync([medicine.HospitalId], ct);
        var mapped = MapMedicinePriceItem(medicine, hospitalsById, nowLocal);

        return new MedicinePriceDetailResponseDto(
            MedicineId: mapped.Id,
            MedicineCode: mapped.MedicineCode,
            Name: mapped.Name,
            ActiveIngredient: mapped.ActiveIngredient,
            Strength: mapped.Strength,
            DosageForm: mapped.DosageForm,
            DispensingUnit: mapped.DispensingUnit,
            HospitalId: mapped.HospitalId,
            Facility: mapped.Facility,
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

    public async Task<MedicinePriceHistoryResponseDto> GetMedicinePriceHistoryAsync(
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
            throw new ForbiddenException("Bạn không có quyền xem lịch sử giá thuốc thuộc cơ sở y tế khác.");
        }

        var medicine = await ResolveScopedMedicineAsync(
            idOrCode,
            isSystemAdmin,
            actorHospitalId,
            hospitalId,
            asNoTracking: true,
            ct);

        var hospitalsById = await LoadHospitalNamesAsync([medicine.HospitalId], ct);
        var mapped = MapMedicinePriceItem(medicine, hospitalsById, nowLocal);

        return new MedicinePriceHistoryResponseDto(
            MedicineId: mapped.Id,
            MedicineCode: mapped.MedicineCode,
            Name: mapped.Name,
            HospitalId: mapped.HospitalId,
            PriceHistory: mapped.PriceHistory);
    }

    public async Task<MedicinePriceAdminItemDto> UpdateMedicinePriceAsync(
        long actorUserId,
        string? routeIdOrCode,
        UpdateMedicinePriceAdminRequest? request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (request is null)
        {
            throw new ValidationException("Dữ liệu cập nhật giá thuốc không được để trống.");
        }

        var targetIdentifier = !string.IsNullOrWhiteSpace(routeIdOrCode)
            ? routeIdOrCode.Trim()
            : request.MedicineId.HasValue
                ? request.MedicineId.Value.ToString(CultureInfo.InvariantCulture)
                : request.MedicineCode?.Trim();

        if (string.IsNullOrWhiteSpace(targetIdentifier))
        {
            throw new ValidationException("Vui lòng chỉ định mã hoặc định danh thuốc cần cập nhật giá.");
        }

        var rawPrice = request.PriceAmount ?? request.Price;
        if (!rawPrice.HasValue)
        {
            throw new ValidationException("Vui lòng nhập mức giá thuốc hợp lệ (PriceAmount >= 0).");
        }

        var priceAmount = rawPrice.Value;
        if (priceAmount < 0m)
        {
            throw new ValidationException("Đơn giá thuốc phải lớn hơn hoặc bằng 0 (PriceAmount >= 0).");
        }

        if (priceAmount > MaxAllowedPrice)
        {
            throw new ValidationException("Đơn giá thuốc không được vượt quá 1.000.000.000 đ.");
        }

        if (decimal.Round(priceAmount, 2) != priceAmount)
        {
            throw new ValidationException("Đơn giá thuốc tối đa 2 chữ số thập phân.");
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
            throw new ForbiddenException("Bạn không có quyền cập nhật giá thuốc thuộc cơ sở y tế khác.");
        }

        var initialMedicine = await ResolveScopedMedicineAsync(
            targetIdentifier,
            isSystemAdmin,
            actorHospitalId,
            request.HospitalId,
            asNoTracking: true,
            ct);

        if (!initialMedicine.HospitalId.HasValue || initialMedicine.HospitalId.Value <= 0)
        {
            throw new ValidationException("Thuốc chưa được gắn cơ sở y tế hợp lệ.");
        }

        var hospital = await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == initialMedicine.HospitalId.Value, ct)
            ?? throw new NotFoundException("Hospital", initialMedicine.HospitalId.Value);

        await using var tx = await AppointmentBookingService.BeginTransactionSafeAsync(_db, ct);

        Medicine medicine;
        DateTime nowLocal;

        try
        {
            // Serialize all effective-interval updates for one medicine (replicates service_prices).
            await _db.TryLockMedicineForUpdateAsync(initialMedicine.Id, ct);

            if (AppointmentBookingService.IsRelationalDatabase(_db) && _db is DbContext efDb)
            {
                efDb.ChangeTracker.Clear();
            }

            medicine = await _db.Medicines
                .Include(m => m.MedicinePrices)
                .FirstOrDefaultAsync(m => m.Id == initialMedicine.Id, ct)
                ?? throw new NotFoundException("Medicine", initialMedicine.Id);

            nowUtc = HospitalClock.GetUtcNow(_timeProvider);
            nowLocal = HospitalClock.GetNow(_timeProvider);
            var todayLocal = HospitalClock.GetToday(_timeProvider);

            var (effectiveFrom, startDateOnly) = ParseValidFrom(rawValidFrom, nowLocal, todayLocal);
            var effectiveTo = ParseValidTo(request.ValidTo, startDateOnly, effectiveFrom);

            var previousActivePrice = ResolveActivePriceRecord(medicine.MedicinePrices, nowLocal);
            var previousActivePriceAmount = previousActivePrice?.PriceAmount;

            var closedIds = new List<long>();
            var resumedPricesToInsert = new List<MedicinePrice>();

            var validExistingPrices = medicine.MedicinePrices
                .Where(p => p.EffectiveTo == null || p.EffectiveTo.Value > p.EffectiveFrom)
                .OrderBy(p => p.EffectiveFrom)
                .ThenBy(p => p.Id)
                .ToList();

            var overlappingPrices = validExistingPrices
                .Where(p => IntervalsOverlap(
                    p.EffectiveFrom,
                    p.EffectiveTo,
                    effectiveFrom,
                    effectiveTo))
                .ToList();

            foreach (var existing in overlappingPrices)
            {
                var existingStart = existing.EffectiveFrom;
                var existingEnd = existing.EffectiveTo;

                // Reject any overlapping scheduled/future window starting at or after effectiveFrom,
                // or any bounded future window already scheduled ahead of nowLocal.
                if (existingStart >= effectiveFrom ||
                    (existingStart > nowLocal && existingEnd.HasValue))
                {
                    throw new ConflictException(
                        "Khoảng thời gian hiệu lực bị chồng lấn với bảng giá thuốc đã được lên lịch trước đó. Vui lòng chọn khoảng hiệu lực không chồng lấn.");
                }

                if (effectiveTo.HasValue)
                {
                    // Bounded promotional/temporary window [effectiveFrom, effectiveTo):
                    // existing baseline must cover the window so it can be cleanly split and resumed.
                    if (existingEnd.HasValue && existingEnd.Value < effectiveTo.Value)
                    {
                        throw new ConflictException(
                            "Khoảng thời gian hiệu lực khuyến mãi/tạm thời vượt quá mốc kết thúc của bảng giá thuốc hiện hành.");
                    }

                    var originalEnd = existingEnd;
                    existing.EffectiveTo = effectiveFrom;
                    closedIds.Add(existing.Id);

                    if (!originalEnd.HasValue || originalEnd.Value > effectiveTo.Value)
                    {
                        resumedPricesToInsert.Add(new MedicinePrice
                        {
                            HospitalId = medicine.HospitalId!.Value,
                            MedicineId = medicine.Id,
                            PriceAmount = existing.PriceAmount,
                            EffectiveFrom = effectiveTo.Value,
                            EffectiveTo = originalEnd,
                            DecisionNumber = existing.DecisionNumber,
                            AppliedBy = existing.AppliedBy,
                            Note = existing.Note,
                            CreatedAt = DateTime.SpecifyKind(nowUtc, DateTimeKind.Unspecified),
                            CreatedByUserId = actorUserId,
                        });
                    }
                }
                else
                {
                    // Open-ended replacement [effectiveFrom, +inf): close prior baseline cleanly at effectiveFrom.
                    existing.EffectiveTo = effectiveFrom;
                    closedIds.Add(existing.Id);
                }
            }

            var newPrice = new MedicinePrice
            {
                HospitalId = medicine.HospitalId!.Value,
                MedicineId = medicine.Id,
                PriceAmount = priceAmount,
                EffectiveFrom = effectiveFrom,
                EffectiveTo = effectiveTo,
                DecisionNumber = decisionNumber,
                AppliedBy = appliedByInput ?? actorDisplayName,
                Note = noteInput,
                CreatedAt = DateTime.SpecifyKind(nowUtc, DateTimeKind.Unspecified),
                CreatedByUserId = actorUserId,
            };

            medicine.MedicinePrices.Add(newPrice);
            _db.MedicinePrices.Add(newPrice);

            foreach (var resumed in resumedPricesToInsert)
            {
                medicine.MedicinePrices.Add(resumed);
                _db.MedicinePrices.Add(resumed);
            }

            await _db.SaveChangesAsync(ct);


            var resolvedAppliedBy = appliedByInput ?? actorDisplayName;
            var resolvedReason = noteInput
                ?? (!string.IsNullOrWhiteSpace(decisionNumber)
                    ? $"Cập nhật giá thuốc theo quyết định {decisionNumber}"
                    : $"Cập nhật giá thuốc {medicine.MedicineCode ?? medicine.Name}");

            var parsedIp = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var ip)
                ? ip
                : null;

            var auditPayload = new MedicinePriceAuditPayload(
                MedicinePriceId: newPrice.Id,
                MedicineId: medicine.Id,
                MedicineCode: medicine.MedicineCode,
                MedicineName: medicine.Name,
                PreviousPriceAmount: previousActivePriceAmount,
                PriceAmount: priceAmount,
                ClosedPriceIds: closedIds,
                ValidFrom: effectiveFrom,
                ValidTo: effectiveTo,
                DecisionNumber: decisionNumber,
                AppliedBy: resolvedAppliedBy,
                Note: noteInput ?? resolvedReason);

            _db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                HospitalId = medicine.HospitalId,
                Action = MedicinePriceUpdatedAction,
                EntityType = MedicineEntityType,
                EntityId = medicine.Id.ToString(CultureInfo.InvariantCulture),
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
            throw new ConflictException("Bảng giá thuốc đang được cập nhật đồng thời bởi quản trị viên khác. Vui lòng tải lại và thử lại.");
        }

        var hospitalsById = new Dictionary<long, string>
        {
            [hospital.Id] = hospital.Name,
        };

        return MapMedicinePriceItem(medicine, hospitalsById, nowLocal);
    }

    private async Task<Medicine> ResolveScopedMedicineAsync(
        string idOrCode,
        bool isSystemAdmin,
        long? actorHospitalId,
        long? requestedHospitalId,
        bool asNoTracking,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã hoặc định danh thuốc không được để trống.");
        }

        var trimmed = idOrCode.Trim();
        if (trimmed.Length > 80)
        {
            throw new ValidationException("Mã hoặc định danh thuốc không được vượt quá 80 ký tự.");
        }

        if (isSystemAdmin && requestedHospitalId is <= 0)
        {
            throw new ValidationException("Mã cơ sở y tế không hợp lệ.");
        }

        var query = asNoTracking
            ? _db.Medicines.AsNoTracking()
            : _db.Medicines.AsQueryable();

        query = query.Include(m => m.MedicinePrices);

        var isNumeric = long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId) && parsedId > 0;

        Medicine? medicine;
        if (isNumeric)
        {
            medicine = await query.FirstOrDefaultAsync(m => m.Id == parsedId, ct);
            if (medicine is null)
            {
                var upperNumeric = trimmed.ToUpperInvariant();
                medicine = await query.FirstOrDefaultAsync(
                    m => (m.MedicineCode ?? string.Empty).ToUpper() == upperNumeric,
                    ct);
            }
        }
        else
        {
            var upper = trimmed.ToUpperInvariant();
            var targetHospId = !isSystemAdmin ? actorHospitalId : requestedHospitalId;

            if (targetHospId.HasValue)
            {
                medicine = await query.FirstOrDefaultAsync(
                    m => m.HospitalId == targetHospId.Value &&
                         (m.MedicineCode ?? string.Empty).ToUpper() == upper,
                    ct);

                if (medicine is null && !isSystemAdmin)
                {
                    var existsInOtherHospital = await _db.Medicines
                        .AsNoTracking()
                        .AnyAsync(m =>
                            (m.MedicineCode ?? string.Empty).ToUpper() == upper &&
                            m.HospitalId != targetHospId.Value, ct);
                    if (existsInOtherHospital)
                    {
                        throw new ForbiddenException("Bạn không có quyền thao tác trên thuốc thuộc cơ sở y tế khác.");
                    }
                }
            }
            else
            {
                medicine = await query
                    .OrderBy(m => m.Id)
                    .FirstOrDefaultAsync(
                        m => (m.MedicineCode ?? string.Empty).ToUpper() == upper,
                        ct);
            }
        }

        if (medicine is null)
        {
            throw new NotFoundException("Medicine", trimmed);
        }

        if (!isSystemAdmin && (!medicine.HospitalId.HasValue || medicine.HospitalId.Value != actorHospitalId!.Value))
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên thuốc thuộc cơ sở y tế khác.");
        }

        if (isSystemAdmin && requestedHospitalId.HasValue && medicine.HospitalId != requestedHospitalId.Value)
        {
            throw new NotFoundException("Medicine", trimmed);
        }

        return medicine;
    }

    private async Task<Dictionary<long, string>> LoadHospitalNamesAsync(
        IEnumerable<long?> hospitalIds,
        CancellationToken ct)
    {
        var ids = hospitalIds
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<long, string>();
        }

        return await _db.Hospitals
            .AsNoTracking()
            .Where(h => ids.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.Name, ct);
    }

    private static MedicinePriceAdminItemDto MapMedicinePriceItem(
        Medicine medicine,
        IReadOnlyDictionary<long, string> hospitalsById,
        DateTime atLocal)
    {
        var isActive = medicine.IsActive ?? true;
        var status = isActive ? "ACTIVE" : "SUSPENDED";
        var facility = medicine.HospitalId.HasValue &&
                       hospitalsById.TryGetValue(medicine.HospitalId.Value, out var hospitalName) &&
                       !string.IsNullOrWhiteSpace(hospitalName)
            ? hospitalName.Trim()
            : UnknownFacilityFallback;

        var orderedPrices = medicine.MedicinePrices
            .Where(p => p.PriceAmount >= 0m)
            .Where(p => p.EffectiveTo == null || p.EffectiveTo.Value > p.EffectiveFrom)
            .OrderByDescending(p => p.EffectiveFrom)
            .ThenByDescending(p => p.Id)
            .ToList();

        var activeRecord = ResolveActivePriceRecord(orderedPrices, atLocal);
        var displayRecord = activeRecord ?? orderedPrices.FirstOrDefault();

        var historyDtos = orderedPrices.Select(p =>
        {
            var amount = p.PriceAmount;
            var isRowActive = activeRecord is not null && activeRecord.Id == p.Id;
            var rowStatus = isRowActive
                ? "ACTIVE"
                : (p.EffectiveFrom > atLocal ? "SCHEDULED" : "EXPIRED");

            return new MedicinePriceHistoryEntryDto(
                Id: p.Id,
                PriceAmount: amount,
                PriceFormatted: FormatVnd(amount),
                ValidFrom: p.EffectiveFrom,
                ValidTo: p.EffectiveTo,
                Date: p.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DecisionNumber: p.DecisionNumber,
                Note: p.Note,
                AppliedBy: p.AppliedBy,
                IsActive: isRowActive,
                Status: rowStatus);
        }).ToList();

        var currentPriceAmount = activeRecord?.PriceAmount;
        var hasEffectivePrice = currentPriceAmount.HasValue;
        var resolvedPrice = currentPriceAmount ?? displayRecord?.PriceAmount ?? 0m;
        var priceFormatted = hasEffectivePrice || displayRecord is not null
            ? FormatVnd(resolvedPrice)
            : UnknownFacilityFallback;

        var effectiveDate = displayRecord?.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new MedicinePriceAdminItemDto(
            Id: medicine.Id,
            MedicineCode: medicine.MedicineCode,
            Name: medicine.Name,
            ActiveIngredient: medicine.ActiveIngredient,
            Strength: medicine.Strength,
            DosageForm: medicine.DosageForm,
            DispensingUnit: medicine.DispensingUnit,
            HospitalId: medicine.HospitalId,
            Facility: facility,
            IsActive: isActive,
            Status: status,
            Price: resolvedPrice,
            CurrentPriceAmount: currentPriceAmount,
            HasEffectivePrice: hasEffectivePrice,
            PriceFormatted: priceFormatted,
            ValidFrom: displayRecord?.EffectiveFrom,
            ValidTo: displayRecord?.EffectiveTo,
            EffectiveDate: effectiveDate,
            DecisionNumber: displayRecord?.DecisionNumber,
            AppliedBy: displayRecord?.AppliedBy,
            Note: displayRecord?.Note,
            PriceHistory: historyDtos);
    }

    private static MedicinePrice? ResolveActivePriceRecord(
        IEnumerable<MedicinePrice> prices,
        DateTime atLocal)
    {
        return prices
            .Where(p => p.PriceAmount >= 0m)
            .Where(p => p.EffectiveFrom <= atLocal)
            .Where(p => p.EffectiveTo == null || p.EffectiveTo.Value > atLocal)
            .OrderByDescending(p => p.EffectiveFrom)
            .ThenByDescending(p => p.Id)
            .FirstOrDefault();
    }

    private static MedicinePricingMetricsDto BuildMetrics(IReadOnlyList<MedicinePriceAdminItemDto> items)
    {
        if (items.Count == 0)
        {
            return new MedicinePricingMetricsDto(
                TotalMedicines: 0,
                PricedMedicinesCount: 0,
                PricedRatioPercent: 0,
                AveragePrice: 0m,
                MinPrice: 0m,
                MaxPrice: 0m,
                LatestEffectiveDate: null);
        }

        var pricedCount = items.Count(i => i.HasEffectivePrice);
        var ratio = (int)Math.Round((double)pricedCount * 100.0 / items.Count);
        var prices = items.Select(i => i.Price).ToList();
        var avg = Math.Round(prices.Average(), 0, MidpointRounding.AwayFromZero);
        var min = prices.Min();
        var max = prices.Max();

        var latestWithEffective = items
            .Where(i => i.ValidFrom.HasValue)
            .OrderByDescending(i => i.ValidFrom!.Value)
            .FirstOrDefault();

        return new MedicinePricingMetricsDto(
            TotalMedicines: items.Count,
            PricedMedicinesCount: pricedCount,
            PricedRatioPercent: ratio,
            AveragePrice: avg,
            MinPrice: min,
            MaxPrice: max,
            LatestEffectiveDate: latestWithEffective?.EffectiveDate);
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

    private static DateTime ParseEffectiveAt(string? raw, DateTime nowLocal)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return nowLocal;
        }

        var trimmed = raw.Trim();
        if (DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            return dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        }

        return ParseHospitalTimestamp(
            trimmed,
            "Thời điểm tra giá hiệu lực (EffectiveAt) không hợp lệ. Vui lòng dùng định dạng YYYY-MM-DD.");
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
            throw new ForbiddenException("Người dùng không có quyền quản trị bảng giá thuốc.");
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
            : (actorUser.Email ?? "Quản trị viên");

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

    private sealed record MedicinePriceAuditPayload(
        long MedicinePriceId,
        long MedicineId,
        string? MedicineCode,
        string MedicineName,
        decimal? PreviousPriceAmount,
        decimal PriceAmount,
        IReadOnlyList<long> ClosedPriceIds,
        DateTime ValidFrom,
        DateTime? ValidTo,
        string? DecisionNumber,
        string? AppliedBy,
        string? Note);
}
