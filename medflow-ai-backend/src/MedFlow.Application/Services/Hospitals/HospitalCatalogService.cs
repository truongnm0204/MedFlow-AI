using MedFlow.Application.DTOs.Hospitals;
using MedFlow.Application.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Hospitals;

public sealed class HospitalCatalogService : IHospitalCatalogService
{
    private readonly IApplicationDbContext _db;

    public HospitalCatalogService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<HospitalDto>> ListAsync(CancellationToken ct = default)
    {
        return await _db.Hospitals
            .AsNoTracking()
            .OrderByDescending(h => h.IsActive ?? true)
            .ThenBy(h => h.Name)
            .ThenBy(h => h.Id)
            .Select(h => new HospitalDto(
                h.Id,
                h.HospitalCode ?? string.Empty,
                h.Name ?? string.Empty,
                h.Address,
                h.IsActive ?? true))
            .ToListAsync(ct);
    }
}
