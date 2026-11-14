using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
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

    private const string ForbiddenMessage = "Bạn không có quyền xem khoản phí của lượt khám này.";
    private const string StaffMissingHospitalMessage = "Tài khoản nhân sự chưa được gán cơ sở y tế.";
    private const string InactiveStaffMessage = "Hồ sơ nhân sự của người dùng hiện không ở trạng thái hoạt động.";
    private const string InactiveHospitalMessage = "Cơ sở y tế của nhân sự hiện đang ngừng hoạt động.";

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
        var actor = await LoadActorAsync(actorUserId, ct);
        var parsed = VisitPaymentAccess.ParseVisitIdentifier(idOrCode);
        var visit = await VisitPaymentAccess.FindVisitAsync(_db, parsed, ct);

        if (actor.IsPatient)
        {
            if (visit is null || !actor.OwnedPatientIds.Contains(visit.PatientId ?? 0))
                throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
        }
        else if (visit is null)
        {
            throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
        }

        var hospitalIds = await VisitHospitalResolver.CollectAsync(_db, visit, ct);
        if (hospitalIds.Count == 0)
            throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
        if (hospitalIds.Count > 1)
            throw new ConflictException(VisitPaymentAccess.HospitalConflictMessage);

        var visitHospitalId = hospitalIds.First();
        if (!actor.IsPatient && !actor.IsSystemAdmin && actor.HospitalId != visitHospitalId)
            throw new ForbiddenException(ForbiddenMessage);

        var assessment = await VisitChargeAssessor.AssessAsync(_db, _timeProvider, visit, ct);
        return new VisitChargesResponseDto(
            visit.Id,
            visit.VisitCode,
            visit.Status,
            visit.PatientId,
            assessment.Lines,
            assessment.PayableTotal,
            assessment.HasUnpricedPayableCharges,
            assessment.AmountDue,
            CurrencyVnd,
            assessment.RecognizedPaidTotal,
            assessment.SettlementStatus,
            assessment.PaymentId,
            assessment.PaymentCode,
            assessment.PaymentStatus,
            assessment.PaymentAmount,
            assessment.PaymentMethod,
            assessment.PaidAt,
            assessment.ExternalReference,
            assessment.CanPayByQr);
    }

    private async Task<ActorContext> LoadActorAsync(long actorUserId, CancellationToken ct)
    {
        await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);

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

        var ids = await VisitPaymentAccess.LoadOwnedPatientIdsAsync(_db, actorUserId, _timeProvider, ct);
        return new ActorContext(true, false, null, ids);
    }

    private static bool HasRole(IEnumerable<string> codes, string role) =>
        codes.Any(c => string.Equals(c, role, StringComparison.OrdinalIgnoreCase));

    private sealed record ActorContext(
        bool IsPatient,
        bool IsSystemAdmin,
        long? HospitalId,
        HashSet<long> OwnedPatientIds);
}
