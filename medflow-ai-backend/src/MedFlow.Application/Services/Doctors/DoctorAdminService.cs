using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
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

    public async Task<DoctorAdminListItemDto> CreateDoctorAsync(
        CreateDoctorRequest request,
        CancellationToken ct = default)
    {
        // 1. Null guard
        ArgumentNullException.ThrowIfNull(request);

        // 2. Validate and normalize DoctorCode
        var rawCode = !string.IsNullOrWhiteSpace(request.DoctorCode)
            ? request.DoctorCode
            : (!string.IsNullOrWhiteSpace(request.Code) ? request.Code : request.Id);
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            throw new ArgumentException("Mã bác sĩ không được để trống.", nameof(request.DoctorCode));
        }

        var doctorCode = rawCode.Trim().ToUpperInvariant();
        if (doctorCode.Length < 2)
        {
            throw new ArgumentException("Mã bác sĩ phải có ít nhất 2 ký tự.", nameof(request.DoctorCode));
        }

        if (doctorCode.Length > 40)
        {
            throw new ArgumentException("Mã bác sĩ không được vượt quá 40 ký tự.", nameof(request.DoctorCode));
        }

        // 3. Validate optional scalar fields before DB queries
        int? rawExperience = request.YearsOfExperience.HasValue ? request.YearsOfExperience.Value : request.Experience;
        if (rawExperience.HasValue && (rawExperience.Value < 0 || rawExperience.Value > 70))
        {
            throw new ArgumentException("Số năm kinh nghiệm phải từ 0 đến 70.", nameof(request.YearsOfExperience));
        }
        short? yearsOfExperience = rawExperience.HasValue ? (short)rawExperience.Value : null;

        var rawTitle = !string.IsNullOrWhiteSpace(request.ProfessionalTitle) ? request.ProfessionalTitle : request.Degree;
        string? professionalTitle = string.IsNullOrWhiteSpace(rawTitle) ? null : rawTitle.Trim();
        if (professionalTitle is not null && professionalTitle.Length > 120)
        {
            throw new ArgumentException("Học hàm / học vị không được vượt quá 120 ký tự.", nameof(request.ProfessionalTitle));
        }

        var rawLicense = !string.IsNullOrWhiteSpace(request.LicenseNumber) ? request.LicenseNumber : request.License;
        string? licenseNumber = string.IsNullOrWhiteSpace(rawLicense) ? null : rawLicense.Trim();
        if (licenseNumber is not null && licenseNumber.Length > 100)
        {
            throw new ArgumentException("Số chứng chỉ hành nghề không được vượt quá 100 ký tự.", nameof(request.LicenseNumber));
        }

        var rawBio = !string.IsNullOrWhiteSpace(request.ProfessionalBio) ? request.ProfessionalBio : request.Biography;
        string? professionalBio = string.IsNullOrWhiteSpace(rawBio) ? null : rawBio.Trim();
        if (professionalBio is not null && professionalBio.Length > 1000)
        {
            throw new ArgumentException("Tiểu sử chuyên môn không được vượt quá 1000 ký tự.", nameof(request.ProfessionalBio));
        }

        var rawQualifications = !string.IsNullOrWhiteSpace(request.QualificationsSummary) ? request.QualificationsSummary : request.Scope;
        string? qualificationsSummary = string.IsNullOrWhiteSpace(rawQualifications) ? null : rawQualifications.Trim();
        if (qualificationsSummary is not null && qualificationsSummary.Length > 2000)
        {
            throw new ArgumentException("Tóm tắt bằng cấp / phạm vi chuyên môn không được vượt quá 2000 ký tự.", nameof(request.QualificationsSummary));
        }

        var rawImage = !string.IsNullOrWhiteSpace(request.ProfileImageUrl) ? request.ProfileImageUrl : request.Portrait;
        string? profileImageUrl = string.IsNullOrWhiteSpace(rawImage) ? null : rawImage.Trim();

        bool isActive;
        if (request.IsActive.HasValue)
        {
            isActive = request.IsActive.Value;
        }
        else if (request.Active.HasValue)
        {
            isActive = request.Active.Value;
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var st = request.Status.Trim();
            if (string.Equals(st, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                isActive = true;
            }
            else if (string.Equals(st, "INACTIVE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(st, "SUSPENDED", StringComparison.OrdinalIgnoreCase))
            {
                isActive = false;
            }
            else
            {
                throw new ArgumentException("Trạng thái hoạt động không hợp lệ. Chỉ chấp nhận ACTIVE, INACTIVE hoặc SUSPENDED.", nameof(request.Status));
            }
        }
        else
        {
            isActive = true;
        }

        // 4. Resolve and validate Staff (mandatory prerequisite)
        Staff? staff;
        if (request.StaffId.HasValue)
        {
            if (request.StaffId.Value <= 0)
            {
                throw new ArgumentException("Mã nhân sự không hợp lệ.", nameof(request.StaffId));
            }

            staff = await _db.Staff
                .Include(s => s.Department)
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.Id == request.StaffId.Value, ct);

            if (staff is null)
            {
                throw new KeyNotFoundException($"Không tìm thấy nhân sự với ID {request.StaffId.Value}.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.StaffCode))
        {
            var staffCode = request.StaffCode.Trim();
            staff = await _db.Staff
                .Include(s => s.Department)
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.StaffCode.ToLower() == staffCode.ToLower(), ct);

            if (staff is null)
            {
                throw new KeyNotFoundException($"Không tìm thấy nhân sự với mã '{staffCode}'.");
            }
        }
        else
        {
            throw new ArgumentException("Nhân sự liên kết (StaffId hoặc StaffCode) không được để trống.", nameof(request.StaffId));
        }

        if (!string.IsNullOrWhiteSpace(staff.Status) &&
            !string.Equals(staff.Status.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Nhân sự '{staff.StaffCode}' không ở trạng thái hoạt động (ACTIVE) nên không thể tạo hồ sơ bác sĩ.", nameof(request.StaffId));
        }

        // 5. Validate Hospital scoping
        if (request.HospitalId.HasValue && request.HospitalId.Value <= 0)
        {
            throw new ArgumentException("Mã cơ sở y tế không hợp lệ.", nameof(request.HospitalId));
        }

        if (request.HospitalId.HasValue && staff.HospitalId.HasValue && request.HospitalId.Value != staff.HospitalId.Value)
        {
            throw new ArgumentException("Nhân sự không thuộc cơ sở y tế được chọn.", nameof(request.HospitalId));
        }

        long hospitalId = request.HospitalId ?? staff.HospitalId ?? 1L;

        // 6. Check DoctorCode uniqueness and 1:1 Staff<->Doctor uniqueness (409 Conflict)
        bool codeExists = await _db.Doctors.AnyAsync(d => d.DoctorCode.ToLower() == doctorCode.ToLower(), ct);
        if (codeExists)
        {
            throw new InvalidOperationException($"Mã bác sĩ '{doctorCode}' đã tồn tại trong cơ sở y tế.");
        }

        bool staffAlreadyLinked = await _db.Doctors.AnyAsync(d => d.StaffId == staff.Id, ct);
        if (staffAlreadyLinked)
        {
            throw new InvalidOperationException($"Nhân sự '{staff.StaffCode}' đã được liên kết với một hồ sơ bác sĩ khác.");
        }

        // 7. Resolve and validate Department (optional override or inherited from Staff)
        Department? department = staff.Department;
        if (request.DepartmentId.HasValue)
        {
            if (request.DepartmentId.Value <= 0)
            {
                throw new ArgumentException("Mã khoa phòng không hợp lệ.", nameof(request.DepartmentId));
            }

            department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value, ct);
            if (department is null)
            {
                throw new KeyNotFoundException($"Không tìm thấy khoa phòng với mã {request.DepartmentId.Value}.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.Department))
        {
            var deptQuery = request.Department.Trim().ToLower();
            department = await _db.Departments.FirstOrDefaultAsync(
                d => d.Name.ToLower() == deptQuery || d.DepartmentCode.ToLower() == deptQuery,
                ct);
            if (department is null)
            {
                throw new KeyNotFoundException($"Không tìm thấy khoa phòng: {request.Department.Trim()}.");
            }
        }

        if (department is not null)
        {
            if (department.IsActive == false)
            {
                throw new ArgumentException($"Khoa phòng '{department.Name}' đang tạm ngưng hoạt động.", nameof(request.DepartmentId));
            }

            if (department.HospitalId.HasValue && department.HospitalId.Value != hospitalId)
            {
                throw new ArgumentException("Khoa phòng không thuộc cùng cơ sở y tế với bác sĩ.", nameof(request.DepartmentId));
            }

            if (staff.DepartmentId != department.Id)
            {
                staff.DepartmentId = department.Id;
                staff.Department = department;
            }
        }

        // 8. Resolve and validate Specialty (optional)
        Specialty? specialty = null;
        if (request.SpecialtyId.HasValue)
        {
            if (request.SpecialtyId.Value <= 0)
            {
                throw new ArgumentException("Mã chuyên khoa không hợp lệ.", nameof(request.SpecialtyId));
            }

            specialty = await _db.Specialties.FirstOrDefaultAsync(s => s.Id == request.SpecialtyId.Value, ct);
            if (specialty is null)
            {
                throw new KeyNotFoundException($"Không tìm thấy chuyên khoa với mã {request.SpecialtyId.Value}.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.Specialty))
        {
            var specQuery = request.Specialty.Trim().ToLower();
            specialty = await _db.Specialties.FirstOrDefaultAsync(
                s => s.Name.ToLower() == specQuery || s.SpecialtyCode.ToLower() == specQuery,
                ct);
            if (specialty is null)
            {
                throw new KeyNotFoundException($"Không tìm thấy chuyên khoa: {request.Specialty.Trim()}.");
            }
        }

        if (specialty is not null && specialty.IsActive == false)
        {
            throw new ArgumentException($"Chuyên khoa '{specialty.Name}' đang tạm ngưng hoạt động.", nameof(request.SpecialtyId));
        }

        // 9. Persist Doctor and return DoctorAdminListItemDto
        var doctor = new Doctor
        {
            StaffId = staff.Id,
            HospitalId = hospitalId,
            DoctorCode = doctorCode,
            SpecialtyId = specialty?.Id,
            ProfessionalTitle = professionalTitle,
            IsActive = isActive,
            ProfileImageUrl = profileImageUrl,
            YearsOfExperience = yearsOfExperience,
            LicenseNumber = licenseNumber,
            ProfessionalBio = professionalBio,
            QualificationsSummary = qualificationsSummary
        };

        _db.Doctors.Add(doctor);
        await _db.SaveChangesAsync(ct);

        return new DoctorAdminListItemDto(
            Id: doctor.Id,
            DoctorCode: doctor.DoctorCode,
            StaffId: staff.Id,
            StaffCode: staff.StaffCode,
            FullName: staff.FullName,
            Email: staff.User?.Email,
            Phone: string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Degree: doctor.ProfessionalTitle,
            SpecialtyId: specialty?.Id,
            SpecialtyName: specialty?.Name,
            DepartmentId: department?.Id ?? staff.DepartmentId,
            DepartmentName: department?.Name,
            LicenseNumber: doctor.LicenseNumber,
            YearsOfExperience: doctor.YearsOfExperience,
            IsActive: doctor.IsActive,
            ProfileImageUrl: doctor.ProfileImageUrl,
            ProfessionalBio: doctor.ProfessionalBio,
            QualificationsSummary: doctor.QualificationsSummary
        );
    }
}

