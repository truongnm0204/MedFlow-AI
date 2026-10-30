using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Doctors;

public sealed class DoctorAdminService : IDoctorAdminService
{
    private readonly IApplicationDbContext _db;

    public DoctorAdminService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<DoctorAdminListResponseDto> GetDoctorListAsync(
        GetDoctorListAdminRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        int page = request.Page < 1 ? 1 : request.Page;
        int pageSize = request.PageSize < 1 ? 10 : (request.PageSize > 100 ? 100 : request.PageSize);

        var query = _db.Doctors
            .Include(d => d.Staff)
                .ThenInclude(s => s!.Department)
            .Include(d => d.Staff)
                .ThenInclude(s => s!.User)
            .Include(d => d.Specialty)
            .AsNoTracking();

        // 1. Lọc theo từ khóa tìm kiếm (Mã BS, Họ tên, CCHN, Chuyên khoa, Mã NV)
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(d =>
                d.DoctorCode.ToLower().Contains(keyword) ||
                (d.Staff != null && d.Staff.FullName.ToLower().Contains(keyword)) ||
                (d.Staff != null && d.Staff.StaffCode.ToLower().Contains(keyword)) ||
                (d.LicenseNumber != null && d.LicenseNumber.ToLower().Contains(keyword)) ||
                (d.Specialty != null && d.Specialty.Name.ToLower().Contains(keyword)));
        }

        // 2. Lọc theo Khoa phòng
        if (request.DepartmentId.HasValue)
        {
            query = query.Where(d => d.Staff != null && d.Staff.DepartmentId == request.DepartmentId.Value);
        }

        // 3. Lọc theo Chuyên khoa
        if (request.SpecialtyId.HasValue)
        {
            query = query.Where(d => d.SpecialtyId == request.SpecialtyId.Value);
        }

        // 4. Lọc theo Trạng thái hoạt động
        if (request.IsActive.HasValue)
        {
            query = query.Where(d => d.IsActive == request.IsActive.Value);
        }

        // 5. Đếm tổng số lượng và phân trang
        int total = await query.CountAsync(ct);
        int totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);

        var doctors = await query
            .OrderBy(d => d.DoctorCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // 6. Ánh xạ sang DTO trả về
        var items = doctors.Select(d => new DoctorAdminListItemDto(
            Id: d.Id,
            DoctorCode: d.DoctorCode,
            StaffId: d.StaffId,
            StaffCode: d.Staff?.StaffCode,
            FullName: d.Staff?.FullName ?? string.Empty,
            Email: d.Staff?.User?.Email,
            Phone: null,
            Degree: d.ProfessionalTitle,
            SpecialtyId: d.SpecialtyId,
            SpecialtyName: d.Specialty?.Name,
            DepartmentId: d.Staff?.DepartmentId,
            DepartmentName: d.Staff?.Department?.Name,
            LicenseNumber: d.LicenseNumber,
            YearsOfExperience: d.YearsOfExperience,
            IsActive: d.IsActive,
            ProfileImageUrl: d.ProfileImageUrl,
            ProfessionalBio: d.ProfessionalBio,
            QualificationsSummary: d.QualificationsSummary
        )).ToList();

        return new DoctorAdminListResponseDto(
            Data: items,
            Total: total,
            Page: page,
            PageSize: pageSize,
            TotalPages: totalPages
        );
    }
}

