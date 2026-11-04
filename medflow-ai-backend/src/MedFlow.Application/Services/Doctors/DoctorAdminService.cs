using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Doctors;

public sealed class DoctorAdminService : IDoctorAdminService
{
    private const string DoctorUpdatedAction = "DOCTOR_UPDATED";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public DoctorAdminService(IApplicationDbContext db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        var items = doctors.Select(MapToDto).ToList();

        return new DoctorAdminListResponseDto(
            Data: items,
            Total: total,
            Page: page,
            PageSize: pageSize,
            TotalPages: totalPages
        );
    }

    public Task<DoctorAdminListItemDto> UpdateDoctorAsync(
        long actorUserId,
        long id,
        UpdateDoctorAdminRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (id <= 0)
        {
            throw new ValidationException("ID bác sĩ phải lớn hơn 0.");
        }

        return UpdateDoctorAsync(
            actorUserId,
            id.ToString(CultureInfo.InvariantCulture),
            request,
            ipAddress,
            ct);
    }

    public async Task<DoctorAdminListItemDto> UpdateDoctorAsync(
        long actorUserId,
        string idOrCode,
        UpdateDoctorAdminRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (parsedId, parsedCode) = ParseDoctorIdentifier(idOrCode);

        if (request.SpecialtyId is null &&
            request.Specialty is null &&
            request.DepartmentId is null &&
            request.Department is null &&
            request.ProfessionalTitle is null &&
            request.Degree is null &&
            request.IsActive is null &&
            request.Status is null &&
            request.FullName is null &&
            request.Name is null &&
            request.LicenseNumber is null &&
            request.License is null &&
            request.YearsOfExperience is null &&
            request.ProfileImageUrl is null &&
            request.ProfessionalBio is null &&
            request.Biography is null &&
            request.QualificationsSummary is null)
        {
            throw new ValidationException("Vui lòng cung cấp ít nhất một thông tin cần cập nhật cho bác sĩ.");
        }

        if (request.SpecialtyId.HasValue && request.SpecialtyId.Value <= 0)
        {
            throw new ValidationException("ID chuyên khoa phải lớn hơn 0.");
        }

        if (request.Specialty is not null && string.IsNullOrWhiteSpace(request.Specialty))
        {
            throw new ValidationException("Chuyên khoa không được để trống.");
        }

        if (request.DepartmentId.HasValue && request.DepartmentId.Value <= 0)
        {
            throw new ValidationException("ID khoa phòng phải lớn hơn 0.");
        }

        if (request.Department is not null && string.IsNullOrWhiteSpace(request.Department))
        {
            throw new ValidationException("Khoa phòng không được để trống.");
        }

        var rawTitle = request.ProfessionalTitle ?? request.Degree;
        string? normalizedTitle = null;
        if (rawTitle is not null)
        {
            if (string.IsNullOrWhiteSpace(rawTitle))
            {
                throw new ValidationException("Học hàm / học vị không được để trống.");
            }

            normalizedTitle = rawTitle.Trim();
            if (normalizedTitle.Length > 120)
            {
                throw new ValidationException("Học hàm / học vị không được vượt quá 120 ký tự.");
            }
        }

        bool? targetIsActive = request.IsActive;
        if (request.Status is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Status))
            {
                throw new ValidationException("Trạng thái hoạt động của bác sĩ không hợp lệ. Chỉ chấp nhận ACTIVE, INACTIVE hoặc SUSPENDED.");
            }

            var normalizedStatus = request.Status.Trim().ToUpperInvariant();
            if (normalizedStatus is not ("ACTIVE" or "INACTIVE" or "SUSPENDED"))
            {
                throw new ValidationException("Trạng thái hoạt động của bác sĩ không hợp lệ. Chỉ chấp nhận ACTIVE, INACTIVE hoặc SUSPENDED.");
            }

            var statusActive = normalizedStatus == "ACTIVE";
            if (request.IsActive.HasValue && request.IsActive.Value != statusActive)
            {
                throw new ValidationException("Trạng thái hoạt động (isActive và status) không đồng nhất.");
            }

            targetIsActive = statusActive;
        }

        var rawFullName = request.FullName ?? request.Name;
        string? normalizedFullName = null;
        if (rawFullName is not null)
        {
            if (string.IsNullOrWhiteSpace(rawFullName))
            {
                throw new ValidationException("Họ và tên bác sĩ không được để trống.");
            }

            normalizedFullName = rawFullName.Trim();
            if (normalizedFullName.Length is < 2 or > 200)
            {
                throw new ValidationException("Họ và tên bác sĩ phải từ 2 đến 200 ký tự.");
            }
        }

        var rawLicense = request.LicenseNumber ?? request.License;
        string? normalizedLicense = null;
        if (rawLicense is not null)
        {
            if (string.IsNullOrWhiteSpace(rawLicense))
            {
                throw new ValidationException("Số chứng chỉ hành nghề không được để trống.");
            }

            normalizedLicense = rawLicense.Trim();
            if (normalizedLicense.Length is < 3 or > 100)
            {
                throw new ValidationException("Số chứng chỉ hành nghề phải từ 3 đến 100 ký tự.");
            }
        }

        if (request.YearsOfExperience.HasValue &&
            (request.YearsOfExperience.Value < 0 || request.YearsOfExperience.Value > 70))
        {
            throw new ValidationException("Số năm kinh nghiệm phải nằm trong khoảng từ 0 đến 70 năm.");
        }

        var rawBio = request.ProfessionalBio ?? request.Biography;
        if (rawBio is not null && rawBio.Trim().Length > 1000)
        {
            throw new ValidationException("Tóm tắt tiểu sử chuyên môn không được vượt quá 1000 ký tự.");
        }

        if (request.QualificationsSummary is not null &&
            request.QualificationsSummary.Trim().Length > 500)
        {
            throw new ValidationException("Tóm tắt bằng cấp chuyên môn không được vượt quá 500 ký tự.");
        }

        if (request.ProfileImageUrl is not null &&
            request.ProfileImageUrl.Trim().Length > 500)
        {
            throw new ValidationException("Đường dẫn ảnh đại diện không được vượt quá 500 ký tự.");
        }

        string? trimmedReason = null;
        if (request.Reason is not null)
        {
            var candidateReason = request.Reason.Trim();
            if (candidateReason.Length > 250)
            {
                throw new ValidationException("Lý do cập nhật không được vượt quá 250 ký tự.");
            }

            trimmedReason = string.IsNullOrWhiteSpace(candidateReason) ? null : candidateReason;
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var (isSystemAdmin, actorHospitalId) = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);

        var doctorQuery = _db.Doctors
            .Include(d => d.Staff)
                .ThenInclude(s => s!.Department)
            .Include(d => d.Staff)
                .ThenInclude(s => s!.User)
            .Include(d => d.Specialty);

        var doctor = parsedId.HasValue
            ? await doctorQuery.FirstOrDefaultAsync(d => d.Id == parsedId.Value, ct)
            : await doctorQuery.FirstOrDefaultAsync(d => d.DoctorCode.ToUpper() == parsedCode, ct);

        if (doctor is null)
        {
            throw new NotFoundException("Doctor", idOrCode.Trim());
        }

        var doctorHospitalId = doctor.HospitalId ?? doctor.Staff?.HospitalId ?? 1L;
        if (!isSystemAdmin && doctorHospitalId != actorHospitalId!.Value)
        {
            throw new ForbiddenException("Bạn không có quyền cập nhật hồ sơ bác sĩ thuộc cơ sở y tế khác.");
        }

        Specialty? resolvedSpecialty = null;
        if (request.SpecialtyId.HasValue)
        {
            resolvedSpecialty = await _db.Specialties
                .FirstOrDefaultAsync(s => s.Id == request.SpecialtyId.Value, ct)
                ?? throw new NotFoundException("Specialty", request.SpecialtyId.Value);
        }
        else if (request.Specialty is not null)
        {
            var specToken = request.Specialty.Trim().ToUpperInvariant();
            resolvedSpecialty = await _db.Specialties
                .FirstOrDefaultAsync(
                    s => s.SpecialtyCode.ToUpper() == specToken || s.Name.ToUpper() == specToken,
                    ct)
                ?? throw new NotFoundException("Specialty", request.Specialty.Trim());
        }

        if (resolvedSpecialty is not null && resolvedSpecialty.IsActive == false)
        {
            throw new ValidationException("Chuyên khoa được chọn đang tạm ngưng hoạt động.");
        }

        Department? resolvedDepartment = null;
        if (request.DepartmentId.HasValue || request.Department is not null)
        {
            if (doctor.Staff is null)
            {
                throw new ConflictException("Hồ sơ bác sĩ chưa được liên kết với nhân sự để gán khoa phòng.");
            }

            if (request.DepartmentId.HasValue)
            {
                resolvedDepartment = await _db.Departments
                    .FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value, ct)
                    ?? throw new NotFoundException("Department", request.DepartmentId.Value);
            }
            else
            {
                var deptToken = request.Department!.Trim().ToUpperInvariant();
                resolvedDepartment = await _db.Departments
                    .FirstOrDefaultAsync(
                        d => (d.HospitalId == null || d.HospitalId == doctorHospitalId) &&
                             (d.DepartmentCode.ToUpper() == deptToken || d.Name.ToUpper() == deptToken),
                        ct)
                    ?? throw new NotFoundException("Department", request.Department.Trim());
            }

            if (resolvedDepartment.HospitalId.HasValue &&
                resolvedDepartment.HospitalId.Value != doctorHospitalId)
            {
                throw new ValidationException("Khoa phòng không thuộc cùng cơ sở y tế với bác sĩ.");
            }

            if (resolvedDepartment.IsActive == false)
            {
                throw new ValidationException("Khoa phòng được chọn đang tạm ngưng hoạt động.");
            }
        }

        if (normalizedFullName is not null && doctor.Staff is null)
        {
            throw new ConflictException("Hồ sơ bác sĩ chưa được liên kết với nhân sự để cập nhật họ tên.");
        }

        if (normalizedLicense is not null)
        {
            var licenseUpper = normalizedLicense.ToUpperInvariant();
            var duplicateLicense = await _db.Doctors
                .AsNoTracking()
                .AnyAsync(
                    d => d.Id != doctor.Id &&
                         d.LicenseNumber != null &&
                         d.LicenseNumber.ToUpper() == licenseUpper,
                    ct);

            if (duplicateLicense)
            {
                throw new ConflictException($"Số chứng chỉ hành nghề '{normalizedLicense}' đã được cấp cho bác sĩ khác.");
            }
        }

        if (targetIsActive.HasValue)
        {
            if (targetIsActive.Value)
            {
                if (doctor.Staff is not null &&
                    !string.Equals(doctor.Staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ConflictException("Không thể kích hoạt bác sĩ khi hồ sơ nhân sự gốc đang ngừng hoạt động.");
                }
            }
            else if (doctor.IsActive)
            {
                var hasActiveVisits = await _db.Visits
                    .AsNoTracking()
                    .AnyAsync(
                        v => v.AssignedDoctorId == doctor.Id &&
                             v.Status != null &&
                             (v.Status.ToUpper() == "WAITING" ||
                              v.Status.ToUpper() == "CALLED" ||
                              v.Status.ToUpper() == "IN_PROGRESS" ||
                              v.Status.ToUpper() == "IN_CONSULTATION"),
                        ct);
                if (hasActiveVisits)
                {
                    throw new ConflictException("Không thể tạm ngưng hoạt động bác sĩ đang có ca khám chưa hoàn thành.");
                }

                var nowLocal = DateTime.SpecifyKind(nowUtc.AddHours(7), DateTimeKind.Unspecified);
                var futureSlotIds = await _db.ExaminationSlots
                    .AsNoTracking()
                    .Where(s => s.DoctorId == doctor.Id && s.StartsAt != null && s.StartsAt > nowLocal)
                    .Select(s => s.Id)
                    .ToListAsync(ct);

                if (futureSlotIds.Count > 0)
                {
                    var hasUpcomingAppointments = await _db.Appointments
                        .AsNoTracking()
                        .AnyAsync(
                            a => a.SlotId != null &&
                                 futureSlotIds.Contains(a.SlotId.Value) &&
                                 a.Status != null &&
                                 (a.Status.ToUpper() == "BOOKED" ||
                                  a.Status.ToUpper() == "CONFIRMED" ||
                                  a.Status.ToUpper() == "CHECKED_IN"),
                            ct);
                    if (hasUpcomingAppointments)
                    {
                        throw new ConflictException("Không thể tạm ngưng hoạt động bác sĩ đang có lịch hẹn sắp tới chưa xử lý.");
                    }
                }
            }
        }

        var previousSnapshot = new
        {
            specialtyId = doctor.SpecialtyId,
            specialtyName = doctor.Specialty?.Name,
            departmentId = doctor.Staff?.DepartmentId,
            departmentName = doctor.Staff?.Department?.Name,
            professionalTitle = doctor.ProfessionalTitle,
            isActive = doctor.IsActive,
            fullName = doctor.Staff?.FullName,
            licenseNumber = doctor.LicenseNumber,
            yearsOfExperience = doctor.YearsOfExperience,
            profileImageUrl = doctor.ProfileImageUrl,
            professionalBio = doctor.ProfessionalBio,
            qualificationsSummary = doctor.QualificationsSummary,
        };

        if (resolvedSpecialty is not null)
        {
            doctor.SpecialtyId = resolvedSpecialty.Id;
            doctor.Specialty = resolvedSpecialty;
        }

        if (resolvedDepartment is not null)
        {
            doctor.Staff!.DepartmentId = resolvedDepartment.Id;
            doctor.Staff.Department = resolvedDepartment;
        }

        if (normalizedTitle is not null)
        {
            doctor.ProfessionalTitle = normalizedTitle;
        }

        if (targetIsActive.HasValue)
        {
            doctor.IsActive = targetIsActive.Value;
        }

        if (normalizedFullName is not null)
        {
            doctor.Staff!.FullName = normalizedFullName;
        }

        if (normalizedLicense is not null)
        {
            doctor.LicenseNumber = normalizedLicense;
        }

        if (request.YearsOfExperience.HasValue)
        {
            doctor.YearsOfExperience = request.YearsOfExperience.Value;
        }

        if (request.ProfileImageUrl is not null)
        {
            doctor.ProfileImageUrl = string.IsNullOrWhiteSpace(request.ProfileImageUrl)
                ? null
                : request.ProfileImageUrl.Trim();
        }

        if (rawBio is not null)
        {
            doctor.ProfessionalBio = string.IsNullOrWhiteSpace(rawBio)
                ? null
                : rawBio.Trim();
        }

        if (request.QualificationsSummary is not null)
        {
            doctor.QualificationsSummary = string.IsNullOrWhiteSpace(request.QualificationsSummary)
                ? null
                : request.QualificationsSummary.Trim();
        }

        var updatedSnapshot = new
        {
            specialtyId = doctor.SpecialtyId,
            specialtyName = doctor.Specialty?.Name,
            departmentId = doctor.Staff?.DepartmentId,
            departmentName = doctor.Staff?.Department?.Name,
            professionalTitle = doctor.ProfessionalTitle,
            isActive = doctor.IsActive,
            fullName = doctor.Staff?.FullName,
            licenseNumber = doctor.LicenseNumber,
            yearsOfExperience = doctor.YearsOfExperience,
            profileImageUrl = doctor.ProfileImageUrl,
            professionalBio = doctor.ProfessionalBio,
            qualificationsSummary = doctor.QualificationsSummary,
        };

        var changeData = JsonSerializer.Serialize(new
        {
            doctorCode = doctor.DoctorCode,
            previous = previousSnapshot,
            updated = updatedSnapshot,
            reason = trimmedReason,
        });

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = doctorHospitalId,
            Action = DoctorUpdatedAction,
            EntityType = "Doctor",
            EntityId = doctor.Id.ToString(CultureInfo.InvariantCulture),
            Reason = trimmedReason,
            ChangeData = changeData,
            IpAddress = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var parsedIp)
                ? parsedIp
                : null,
            OccurredAt = nowUtc,
        });

        await _db.SaveChangesAsync(ct);

        return MapToDto(doctor);
    }

    private static (long? Id, string? Code) ParseDoctorIdentifier(string idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã định danh bác sĩ không được để trống.");
        }

        var trimmed = idOrCode.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId))
        {
            if (numericId <= 0)
            {
                throw new ValidationException("ID bác sĩ phải lớn hơn 0.");
            }

            return (numericId, null);
        }

        if (trimmed.Length > 40 || !trimmed.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_'))
        {
            throw new ValidationException("Mã định danh bác sĩ không hợp lệ.");
        }

        return (null, trimmed.ToUpperInvariant());
    }

    private async Task<(bool IsSystemAdmin, long? ActorHospitalId)> EnsureAdminAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
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

        var actorRoleCodes = await _db.Roles
            .AsNoTracking()
            .Where(r => actorRoleIds.Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync(ct);

        var isSystemAdmin = actorRoleCodes.Any(c =>
            string.Equals(c, "SYSTEM_ADMIN", StringComparison.OrdinalIgnoreCase));
        var isHospitalAdmin = actorRoleCodes.Any(c =>
            string.Equals(c, "HOSPITAL_ADMIN", StringComparison.OrdinalIgnoreCase));

        if (!isSystemAdmin && !isHospitalAdmin)
        {
            throw new ForbiddenException("Người dùng không có quyền cập nhật hồ sơ bác sĩ.");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is not null &&
            !string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của quản trị viên không ở trạng thái hoạt động.");
        }

        if (!isSystemAdmin)
        {
            if (actorStaff is null || !actorStaff.HospitalId.HasValue)
            {
                throw new ForbiddenException("Tài khoản quản trị bệnh viện chưa được gán cơ sở y tế.");
            }

            var hospital = await _db.Hospitals
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == actorStaff.HospitalId.Value, ct);

            if (hospital is not null && hospital.IsActive == false)
            {
                throw new ForbiddenException("Cơ sở y tế của quản trị viên đang ngừng hoạt động.");
            }
        }

        return (isSystemAdmin, actorStaff?.HospitalId);
    }

    private static DoctorAdminListItemDto MapToDto(Doctor d) =>
        new(
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
        );
}

