using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Patients;

public sealed class PatientVisitListService : IPatientVisitListService
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 20;
    public const string ForbiddenMessage = "Bạn không có quyền xem lượt khám.";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public PatientVisitListService(IApplicationDbContext db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PatientVisitListResponseDto> GetMyVisitsAsync(
        long actorUserId,
        int? limit,
        CancellationToken ct = default)
    {
        await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);
        if (!await HasPatientRoleAsync(actorUserId, ct))
            throw new ForbiddenException(ForbiddenMessage);

        var owned = await VisitPaymentAccess.LoadOwnedPatientIdsAsync(_db, actorUserId, _timeProvider, ct);
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        var candidates = await _db.Visits.AsNoTracking()
            .Where(v => v.PatientId != null && owned.Contains(v.PatientId.Value))
            .OrderByDescending(v => v.CheckedInAt)
            .ThenByDescending(v => v.Id)
            .Take(MaxLimit)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return new PatientVisitListResponseDto([]);

        var hospitalMap = await VisitHospitalResolver.CollectManyAsync(_db, candidates, ct);
        var visible = candidates
            .Where(v => hospitalMap[v.Id].Count == 1)
            .Take(take)
            .ToList();

        if (visible.Count == 0)
            return new PatientVisitListResponseDto([]);

        var visitIds = visible.Select(v => v.Id).ToList();
        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.VisitId != null && visitIds.Contains(p.VisitId.Value))
            .OrderByDescending(p => p.Id)
            .ToListAsync(ct);
        var latestPayment = payments
            .GroupBy(p => p.VisitId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var hospitalIds = visible
            .Select(v => hospitalMap[v.Id].First())
            .Distinct()
            .ToList();
        var hospitals = await _db.Hospitals.AsNoTracking()
            .Where(h => hospitalIds.Contains(h.Id))
            .Select(h => new { h.Id, h.Name })
            .ToListAsync(ct);
        var hospitalNames = hospitals.ToDictionary(h => h.Id, h => h.Name);

        var items = visible.Select(visit =>
        {
            latestPayment.TryGetValue(visit.Id, out var payment);
            hospitalNames.TryGetValue(hospitalMap[visit.Id].First(), out var hospitalName);
            return new PatientVisitListItemDto(
                visit.Id,
                visit.VisitCode,
                visit.Status,
                visit.CheckedInAt,
                visit.CompletedAt,
                hospitalName,
                payment?.Status,
                payment?.PaymentCode);
        }).ToList();

        return new PatientVisitListResponseDto(items);
    }

    private async Task<bool> HasPatientRoleAsync(long actorUserId, CancellationToken ct)
    {
        var roleIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);
        var codes = await _db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync(ct);
        return codes.Any(c => string.Equals(c, "PATIENT", StringComparison.OrdinalIgnoreCase));
    }
}
