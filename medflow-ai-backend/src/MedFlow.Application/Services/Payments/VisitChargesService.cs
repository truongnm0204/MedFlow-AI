using System.Globalization;
using System.Text.Json;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Payments;

public sealed class VisitChargesService : IVisitChargesService
{
    public const string ChargeSourcePersisted = "PERSISTED";
    public const string ChargeSourceDerived = "DERIVED";
    public const string ChargeDataValid = "VALID";
    public const string ChargeDataInvalid = "INVALID";
    public const string ChargeDataUnpriced = "UNPRICED";
    public const string SettlementUnpaid = "UNPAID";
    public const string SettlementPaid = "PAID";
    public const string SettlementMismatch = "MISMATCH";
    public const string SettlementUndetermined = "UNDETERMINED";
    public const string CurrencyVnd = "VND";

    private const string VisitNotFoundMessage = "Không tìm thấy lượt khám.";
    private const string InvalidVisitMessage = "Mã lượt khám không hợp lệ.";
    private const string InvalidActorMessage = "Thông tin định danh người dùng không hợp lệ.";
    private const string InactiveActorMessage = "Tài khoản người dùng hiện không ở trạng thái hoạt động.";
    private const string LockedActorMessage = "Tài khoản người dùng hiện đang bị tạm khóa.";
    private const string ForbiddenMessage = "Bạn không có quyền xem khoản phí của lượt khám này.";
    private const string HospitalConflictMessage = "Không xác định được cơ sở của lượt khám.";
    private const string StaffMissingHospitalMessage = "Tài khoản nhân sự chưa được gán cơ sở y tế.";
    private const string InactiveStaffMessage = "Hồ sơ nhân sự của người dùng hiện không ở trạng thái hoạt động.";
    private const string InactiveHospitalMessage = "Cơ sở y tế của nhân sự hiện đang ngừng hoạt động.";

    private const string WalkInAction = "WALK_IN_VISIT_CREATED";
    private const string VisitEntityType = "VISIT";
    private const int MaxIdentifierLength = 40;

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public VisitChargesService(IApplicationDbContext db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<VisitChargesResponseDto> GetAsync(
        long actorUserId,
        string? idOrCode,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
            throw new UnauthorizedAccessException(InvalidActorMessage);

        var actor = await LoadActorAsync(actorUserId, ct);
        var parsed = ParseVisitIdentifier(idOrCode);
        var visit = await FindVisitAsync(parsed, ct);

        if (actor.IsPatient)
        {
            if (visit is null || !actor.OwnedPatientIds.Contains(visit.PatientId ?? 0))
                throw new NotFoundException(VisitNotFoundMessage);
        }
        else if (visit is null)
        {
            throw new NotFoundException(VisitNotFoundMessage);
        }

        var hospitalIds = await CollectHospitalIdsAsync(visit, ct);
        if (hospitalIds.Count == 0)
            throw new NotFoundException(VisitNotFoundMessage);
        if (hospitalIds.Count > 1)
            throw new ConflictException(HospitalConflictMessage);

        var visitHospitalId = hospitalIds.First();
        if (!actor.IsPatient && !actor.IsSystemAdmin && actor.HospitalId != visitHospitalId)
            throw new ForbiddenException(ForbiddenMessage);

        return await BuildResponseAsync(visit, ct);
    }

    private async Task<ActorContext> LoadActorAsync(long actorUserId, CancellationToken ct)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new UnauthorizedAccessException(InvalidActorMessage);

        if (user.AccountStatus != AccountStatus.ACTIVE)
            throw new UnauthorizedAccessException(InactiveActorMessage);

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > nowUtc)
            throw new UnauthorizedAccessException(LockedActorMessage);

        var roleIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var roleCodes = await _db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync(ct);

        var isSystemAdmin = HasRole(roleCodes, "SYSTEM_ADMIN");
        var isHospitalAdmin = HasRole(roleCodes, "HOSPITAL_ADMIN");
        var isReception = HasRole(roleCodes, "RECEPTION");
        var isPatient = HasRole(roleCodes, "PATIENT");

        if (isSystemAdmin || isHospitalAdmin || isReception)
        {
            var staff = await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);
            if (staff is not null && !string.Equals(staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                throw new ForbiddenException(InactiveStaffMessage);

            if (!isSystemAdmin)
            {
                if (staff is null || staff.HospitalId is null)
                    throw new ForbiddenException(StaffMissingHospitalMessage);

                var hospital = await _db.Hospitals.AsNoTracking()
                    .FirstOrDefaultAsync(h => h.Id == staff.HospitalId.Value, ct)
                    ?? throw new NotFoundException("Hospital", staff.HospitalId.Value);

                if (hospital.IsActive == false)
                    throw new ForbiddenException(InactiveHospitalMessage);
            }

            return new ActorContext(false, isSystemAdmin, staff?.HospitalId, []);
        }

        if (!isPatient)
            throw new ForbiddenException(ForbiddenMessage);

        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var owned = await AppointmentBookingRules.LoadOwnedPatientsAsync(_db, actorUserId, nowLocal, ct);
        var ids = new HashSet<long> { owned.Self.Id };
        foreach (var link in owned.FamilyLinks)
            ids.Add(link.MemberPatientId);

        return new ActorContext(true, false, null, ids);
    }

    private async Task<Visit?> FindVisitAsync(ParsedVisit parsed, CancellationToken ct)
    {
        if (parsed.Id is long id)
        {
            return await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);
        }

        var code = parsed.Code!;
        return await _db.Visits.AsNoTracking()
            .FirstOrDefaultAsync(v => v.VisitCode != null && v.VisitCode.ToUpper() == code, ct);
    }

    private async Task<HashSet<long>> CollectHospitalIdsAsync(Visit visit, CancellationToken ct)
    {
        var ids = new HashSet<long>();

        if (visit.AppointmentId is long appointmentId)
        {
            var appointment = await _db.Appointments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);
            if (appointment?.SlotId is long slotId)
            {
                var slot = await _db.ExaminationSlots.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == slotId, ct);
                if (slot?.ServiceId is long serviceId)
                {
                    var serviceHospital = await _db.Services.AsNoTracking()
                        .Where(s => s.Id == serviceId)
                        .Select(s => s.HospitalId)
                        .FirstOrDefaultAsync(ct);
                    AddIfPresent(ids, serviceHospital);
                }

                if (slot?.DoctorId is long doctorId)
                {
                    var doctorHospital = await _db.Doctors.AsNoTracking()
                        .Where(d => d.Id == doctorId)
                        .Select(d => d.HospitalId)
                        .FirstOrDefaultAsync(ct);
                    AddIfPresent(ids, doctorHospital);
                }
            }
        }

        var audits = await LoadWalkInAuditsAsync(visit.Id, ct);
        foreach (var audit in audits)
            AddIfPresent(ids, audit.HospitalId);

        if (visit.ExaminationRoomId is long roomId)
        {
            var roomHospital = await _db.HospitalRooms.AsNoTracking()
                .Where(r => r.Id == roomId)
                .Select(r => r.HospitalId)
                .FirstOrDefaultAsync(ct);
            AddIfPresent(ids, roomHospital);
        }

        if (visit.AssignedDoctorId is long assignedDoctorId)
        {
            var assignedHospital = await _db.Doctors.AsNoTracking()
                .Where(d => d.Id == assignedDoctorId)
                .Select(d => d.HospitalId)
                .FirstOrDefaultAsync(ct);
            AddIfPresent(ids, assignedHospital);
        }

        if (visit.CheckedInByStaffId is long staffId)
        {
            var staffHospital = await _db.Staff.AsNoTracking()
                .Where(s => s.Id == staffId)
                .Select(s => s.HospitalId)
                .FirstOrDefaultAsync(ct);
            AddIfPresent(ids, staffHospital);
        }

        var entries = await _db.QueueEntries.AsNoTracking()
            .Where(q => q.VisitId == visit.Id)
            .ToListAsync(ct);

        var pointIds = entries
            .SelectMany(q => new long?[] { q.ServicePointId, q.CurrentServicePointId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (pointIds.Count > 0)
        {
            var pointHospitals = await _db.QueueServicePoints.AsNoTracking()
                .Where(p => pointIds.Contains(p.Id))
                .Select(p => p.HospitalId)
                .ToListAsync(ct);
            foreach (var hospitalId in pointHospitals)
                AddIfPresent(ids, hospitalId);
        }

        return ids;
    }

    private async Task<VisitChargesResponseDto> BuildResponseAsync(Visit visit, CancellationToken ct)
    {
        var charges = await _db.VisitCharges.AsNoTracking()
            .Where(c => c.VisitId == visit.Id)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.VisitId == visit.Id)
            .OrderByDescending(p => p.Id)
            .ToListAsync(ct);

        var audits = await LoadWalkInAuditsAsync(visit.Id, ct);
        var audit = audits.FirstOrDefault();
        var scheduled = await LoadScheduledPriceAsync(visit, ct);

        List<VisitChargeLineDto> lines;
        if (charges.Count > 0)
        {
            lines = charges.Select(MapPersisted).ToList();
        }
        else
        {
            lines = [await MapDerivedAsync(audit, scheduled, ct)];
        }

        lines = await FillServiceNamesAsync(lines, ct);

        var payableTotal = lines
            .Where(l => l.Payable == true && l.UnitPrice is >= 0)
            .Sum(l => l.UnitPrice!.Value);

        var hasUnpriced = lines.Any(l =>
            l.Payable is null || (l.Payable == true && l.UnitPrice is null));

        var paid = payments
            .Where(p => IsStatus(p.Status, "PAID"))
            .GroupBy(DedupeKey, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(p => p.Id).First())
            .ToList();

        var recognizedPaid = paid.Aggregate(0m, (sum, p) => sum + (p.Amount ?? 0m));
        var paidAmountMissing = paid.Any(p => p.Amount is null);

        string settlement;
        decimal? amountDue;
        if (hasUnpriced || paidAmountMissing)
        {
            settlement = SettlementUndetermined;
            amountDue = null;
        }
        else if (paid.Count == 0)
        {
            settlement = SettlementUnpaid;
            amountDue = payableTotal;
        }
        else if (paid.Count == 1 && paid[0].Amount == payableTotal)
        {
            settlement = SettlementPaid;
            amountDue = 0m;
        }
        else
        {
            settlement = SettlementMismatch;
            amountDue = null;
        }

        var latest = payments.FirstOrDefault();
        var paymentMethod = NormalizePaymentMethod(audit?.PaymentMethod);
        var hasPending = payments.Any(p => IsStatus(p.Status, "PENDING"));
        var visitBlocked = IsStatus(visit.Status, "CANCELLED") || IsStatus(visit.Status, "NO_SHOW");
        var hasInvalid = lines.Any(l => l.ChargeDataStatus == ChargeDataInvalid);

        var canPayByQr = !visitBlocked
            && !hasUnpriced
            && !hasInvalid
            && settlement == SettlementUnpaid
            && amountDue is > 0
            && !hasPending
            && !string.Equals(paymentMethod, "CASHIER", StringComparison.OrdinalIgnoreCase);

        return new VisitChargesResponseDto(
            visit.Id,
            visit.VisitCode,
            visit.Status,
            visit.PatientId,
            lines,
            payableTotal,
            hasUnpriced,
            amountDue,
            CurrencyVnd,
            recognizedPaid,
            settlement,
            latest?.Id,
            latest?.PaymentCode,
            latest is null ? SettlementUnpaid : latest.Status,
            latest?.Amount,
            paymentMethod,
            latest?.PaidAt,
            latest?.ExternalReference,
            canPayByQr);
    }

    private static VisitChargeLineDto MapPersisted(VisitCharge charge)
    {
        var raw = string.IsNullOrWhiteSpace(charge.PaymentRequirement)
            ? null
            : charge.PaymentRequirement.Trim();
        var normalized = raw?.ToUpperInvariant();
        var negative = charge.UnitPrice is < 0;

        bool? payable;
        string dataStatus;
        if (negative || (normalized is not null && normalized is not "REQUIRED" && normalized is not "WAIVED"))
        {
            payable = null;
            dataStatus = ChargeDataInvalid;
        }
        else if (normalized == "WAIVED")
        {
            payable = false;
            dataStatus = ChargeDataValid;
        }
        else if (charge.UnitPrice is null)
        {
            payable = true;
            dataStatus = ChargeDataUnpriced;
        }
        else
        {
            payable = true;
            dataStatus = ChargeDataValid;
        }

        return new VisitChargeLineDto(
            charge.Id,
            ChargeSourcePersisted,
            charge.ServiceId,
            null,
            null,
            charge.DiagnosticOrderId,
            charge.UnitPrice,
            raw,
            payable,
            dataStatus);
    }

    private async Task<VisitChargeLineDto> MapDerivedAsync(
        WalkInAudit? audit,
        ScheduledPrice? scheduled,
        CancellationToken ct)
    {
        decimal? price = null;
        long? serviceId = null;
        string? serviceCode = null;
        string? serviceName = null;
        var invalidPrice = false;

        if (audit?.ServicePrice is decimal auditPrice)
        {
            serviceId = audit.ServiceId;
            serviceCode = audit.ServiceCode;
            serviceName = audit.ServiceName;
            price = auditPrice;
            invalidPrice = auditPrice < 0;
        }
        else if (scheduled is not null)
        {
            serviceId = scheduled.ServiceId;
            serviceCode = scheduled.ServiceCode;
            serviceName = scheduled.ServiceName;
            price = scheduled.Price;
            invalidPrice = scheduled.Price is < 0;
        }

        if (serviceId is long id && (serviceCode is null || serviceName is null))
        {
            var service = await _db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
            serviceCode ??= service?.ServiceCode;
            serviceName ??= service?.Name;
        }

        var dataStatus = invalidPrice
            ? ChargeDataInvalid
            : price is null ? ChargeDataUnpriced : ChargeDataValid;

        return new VisitChargeLineDto(
            null,
            ChargeSourceDerived,
            serviceId,
            serviceCode,
            serviceName,
            null,
            price,
            null,
            invalidPrice ? null : true,
            dataStatus);
    }

    private async Task<List<VisitChargeLineDto>> FillServiceNamesAsync(
        List<VisitChargeLineDto> lines,
        CancellationToken ct)
    {
        var missing = lines
            .Where(l => l.ServiceId is not null && (l.ServiceCode is null || l.ServiceName is null))
            .Select(l => l.ServiceId!.Value)
            .Distinct()
            .ToList();

        if (missing.Count == 0)
            return lines;

        var services = await _db.Services.AsNoTracking()
            .Where(s => missing.Contains(s.Id))
            .ToListAsync(ct);

        return lines.Select(line =>
        {
            if (line.ServiceId is not long id)
                return line;
            var service = services.FirstOrDefault(s => s.Id == id);
            if (service is null)
                return line;
            return line with
            {
                ServiceCode = line.ServiceCode ?? service.ServiceCode,
                ServiceName = line.ServiceName ?? service.Name,
            };
        }).ToList();
    }

    private async Task<ScheduledPrice?> LoadScheduledPriceAsync(Visit visit, CancellationToken ct)
    {
        if (visit.AppointmentId is not long appointmentId)
            return null;

        var appointment = await _db.Appointments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);
        if (appointment?.SlotId is not long slotId)
            return null;

        var slot = await _db.ExaminationSlots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot?.ServiceId is not long serviceId)
            return null;

        var service = await _db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serviceId, ct);
        var reference = visit.CheckedInAt ?? AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var amounts = await _db.ServicePrices.AsNoTracking()
            .Where(p =>
                p.ServiceId == serviceId &&
                p.PriceAmount != null &&
                (p.EffectiveFrom == null || p.EffectiveFrom <= reference) &&
                (p.EffectiveTo == null || p.EffectiveTo >= reference))
            .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
            .ThenByDescending(p => p.Id)
            .Select(p => p.PriceAmount!.Value)
            .ToListAsync(ct);

        return new ScheduledPrice(
            serviceId,
            service?.ServiceCode,
            service?.Name,
            amounts.Count == 0 ? null : amounts[0]);
    }

    private async Task<List<WalkInAudit>> LoadWalkInAuditsAsync(long visitId, CancellationToken ct)
    {
        var entityId = visitId.ToString(CultureInfo.InvariantCulture);
        var rows = await _db.AuditLogs.AsNoTracking()
            .Where(a =>
                a.EntityId == entityId &&
                a.EntityType.ToUpper() == VisitEntityType &&
                a.Action.ToUpper() == WalkInAction)
            .OrderByDescending(a => a.Id)
            .ToListAsync(ct);

        return rows.Select(ParseAudit).ToList();
    }

    private static WalkInAudit ParseAudit(AuditLog row)
    {
        long? serviceId = null;
        string? serviceCode = null;
        string? serviceName = null;
        decimal? servicePrice = null;
        string? paymentMethod = null;

        if (!string.IsNullOrWhiteSpace(row.ChangeData))
        {
            try
            {
                using var doc = JsonDocument.Parse(row.ChangeData);
                var root = doc.RootElement;
                serviceId = ReadInt64(root, "serviceId");
                serviceCode = ReadString(root, "serviceCode");
                serviceName = ReadString(root, "serviceName");
                servicePrice = ReadDecimal(root, "servicePrice");
                paymentMethod = ReadString(root, "paymentMethod");
            }
            catch (JsonException)
            {
                // Malformed audit payload does not fail the read.
            }
        }

        return new WalkInAudit(row.HospitalId, serviceId, serviceCode, serviceName, servicePrice, paymentMethod);
    }

    private static ParsedVisit ParseVisitIdentifier(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
            throw new ValidationException(InvalidVisitMessage);

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
            trimmed = trimmed[1..].Trim();

        if (trimmed.Length == 0 || trimmed.Length > MaxIdentifierLength)
            throw new ValidationException(InvalidVisitMessage);

        var numeric = trimmed[0] == '-'
            ? trimmed.Length > 1 && trimmed[1..].All(static c => c is >= '0' and <= '9')
            : trimmed.All(static c => c is >= '0' and <= '9');
        if (numeric)
        {
            if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
                throw new ValidationException(InvalidVisitMessage);
            return new ParsedVisit(id, null);
        }

        return new ParsedVisit(null, trimmed.ToUpperInvariant());
    }

    private static string DedupeKey(Payment payment)
    {
        if (!string.IsNullOrWhiteSpace(payment.PaymentCode))
            return "code:" + payment.PaymentCode.Trim();
        if (!string.IsNullOrWhiteSpace(payment.ExternalReference))
            return "ref:" + payment.ExternalReference.Trim();
        return "id:" + payment.Id.ToString(CultureInfo.InvariantCulture);
    }

    private static string? NormalizePaymentMethod(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Trim().ToUpperInvariant() switch
        {
            "VIETQR" or "QR" or "BANK_TRANSFER" => "VIETQR",
            "CASHIER" or "CASH" or "COUNTER" => "CASHIER",
            _ => null,
        };
    }

    private static bool HasRole(IEnumerable<string> codes, string role) =>
        codes.Any(c => string.Equals(c, role, StringComparison.OrdinalIgnoreCase));

    private static bool IsStatus(string? status, string expected) =>
        string.Equals(status?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static void AddIfPresent(HashSet<long> ids, long? hospitalId)
    {
        if (hospitalId is > 0)
            ids.Add(hospitalId.Value);
    }

    private static long? ReadInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    private static decimal? ReadDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private sealed record ActorContext(
        bool IsPatient,
        bool IsSystemAdmin,
        long? HospitalId,
        HashSet<long> OwnedPatientIds);

    private sealed record ParsedVisit(long? Id, string? Code);

    private sealed record WalkInAudit(
        long? HospitalId,
        long? ServiceId,
        string? ServiceCode,
        string? ServiceName,
        decimal? ServicePrice,
        string? PaymentMethod);

    private sealed record ScheduledPrice(
        long ServiceId,
        string? ServiceCode,
        string? ServiceName,
        decimal? Price);
}
