using MedFlow.Application.Common.Time;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Pharmacy;

public sealed class MedicinePriceResolver : IMedicinePriceResolver
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public MedicinePriceResolver(IApplicationDbContext db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<MedicinePriceQuote?> TryGetEffectivePriceAsync(
        long hospitalId,
        long medicineId,
        CancellationToken ct = default)
        => TryGetEffectivePriceAsync(
            hospitalId,
            medicineId,
            HospitalClock.GetNow(_timeProvider),
            ct);

    public async Task<MedicinePriceQuote?> TryGetEffectivePriceAsync(
        long hospitalId,
        long medicineId,
        DateTime atHospitalLocal,
        CancellationToken ct = default)
    {
        if (hospitalId <= 0)
        {
            throw new ValidationException("Mã cơ sở y tế không hợp lệ.");
        }

        if (medicineId <= 0)
        {
            throw new ValidationException("Mã thuốc không hợp lệ.");
        }

        var at = atHospitalLocal.Kind == DateTimeKind.Utc
            ? HospitalClock.ConvertUtcToHospitalLocal(atHospitalLocal)
            : DateTime.SpecifyKind(atHospitalLocal, DateTimeKind.Unspecified);

        var quote = await _db.MedicinePrices
            .AsNoTracking()
            .Where(p =>
                p.HospitalId == hospitalId &&
                p.MedicineId == medicineId &&
                p.EffectiveFrom <= at &&
                (p.EffectiveTo == null || p.EffectiveTo > at))
            .OrderByDescending(p => p.EffectiveFrom)
            .ThenByDescending(p => p.Id)
            .Select(p => new MedicinePriceQuote(
                p.Id,
                p.HospitalId,
                p.MedicineId,
                p.PriceAmount,
                p.EffectiveFrom,
                p.EffectiveTo))
            .FirstOrDefaultAsync(ct);

        return quote;
    }
}
