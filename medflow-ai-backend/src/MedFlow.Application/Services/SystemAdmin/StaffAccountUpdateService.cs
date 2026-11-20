using EmailAddressAttribute = System.ComponentModel.DataAnnotations.EmailAddressAttribute;
using System.Text.Json;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.SystemAdmin;

public sealed class StaffAccountUpdateService : IStaffAccountUpdateService
{
    private const string ActiveStatus = "ACTIVE";
    private const string DisabledStatus = "DISABLED";
    private const string LockedStatus = "LOCKED";
    private const string PatientRoleCode = "PATIENT";
    private const string SystemAdminRoleCode = "SYSTEM_ADMIN";
    private const string UpdatedAction = "STAFF_ACCOUNT_UPDATED";

    private static readonly EmailAddressAttribute EmailValidator = new();
    private static readonly HashSet<string> BaselineInactiveRoleCodes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "SECURITY_AUDITOR",
        };
    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IApplicationDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly TimeProvider _timeProvider;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public StaffAccountUpdateService(
        IApplicationDbContext db,
        IAuditLogger audit,
        TimeProvider timeProvider)
    {
        _db = db;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    public async Task<UpdateStaffAccountResponse> UpdateAsync(
        long actorUserId,
        string staffIdentifier,
        UpdateStaffAccountRequest request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await EnsureSystemAdminAccessAsync(actorUserId, nowUtc, ct);
        EnsureHasChanges(request);

        var staff = await FindStaffAsync(staffIdentifier, ct);
        if (staff.UserId is null)
        {
            throw new ValidationException("Hồ sơ nhân sự chưa được liên kết với tài khoản đăng nhập.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == staff.UserId.Value, ct)
            ?? throw new NotFoundException("Không tìm thấy tài khoản đăng nhập của nhân sự.");

        var assignments = await _db.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .OrderBy(ur => ur.RoleId)
            .ToListAsync(ct);
        var roleIds = assignments.Select(ur => ur.RoleId).ToArray();
        var currentRoles = await _db.Roles
            .Where(r => roleIds.Contains(r.Id))
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        if (currentRoles.Count == 0)
        {
            throw new ValidationException("Tài khoản nhân sự chưa được gán vai trò.");
        }

        var isSelf = user.Id == actorUserId;
        if (isSelf &&
            (request.Role is not null || request.Status is not null || request.AuthProvider is not null))
        {
            throw new ForbiddenException(
                "Không thể tự thay đổi vai trò, trạng thái hoặc phương thức đăng nhập của tài khoản quản trị.");
        }
        var isSystemAdminTarget = currentRoles.Any(r =>
            string.Equals(r.Code, SystemAdminRoleCode, StringComparison.OrdinalIgnoreCase));
        if (isSystemAdminTarget &&
            (request.Role is not null || request.Status is not null || request.AuthProvider is not null))
        {
            throw new ForbiddenException(
                "Không thể thay đổi quyền truy cập của tài khoản quản trị hệ thống qua API nhân sự.");
        }


        var previousRoleCode = currentRoles[0].Code;
        var previousStatus = GetEffectiveStatus(user, staff, nowUtc);
        var previousAuthProvider = user.AuthProvider;
        var previousHospitalId = staff.HospitalId;
        var previousDepartmentId = staff.DepartmentId;
        var changedFields = new List<string>(7);

        if (request.FullName is not null)
        {
            staff.FullName = RequiredTrimmed(request.FullName, "Họ tên", 200);
            changedFields.Add(nameof(request.FullName));
        }

        if (request.Email is not null)
        {
            var email = NormalizeEmail(request.Email);
            if (await _db.Users.AnyAsync(u => u.Id != user.Id && u.Email.ToLower() == email, ct))
            {
                throw new ConflictException($"Email '{email}' đã được sử dụng.");
            }

            user.Email = email;
            changedFields.Add(nameof(request.Email));
        }

        if (request.AuthProvider is not null)
        {
            ApplyAuthProvider(user, request.AuthProvider, request.Password);
            changedFields.Add(nameof(request.AuthProvider));
        }
        else if (request.Password is not null)
        {
            throw new ValidationException(
                "Password chỉ được gửi khi chuyển phương thức đăng nhập sang LOCAL.");
        }

        if (request.Status is not null)
        {
            ApplyStatus(user, staff, request.Status, previousStatus);
            changedFields.Add(nameof(request.Status));
        }

        Hospital? hospital;
        Department? department;
        if (request.Facility is not null || request.Department is not null)
        {
            hospital = await ResolveHospitalForUpdateAsync(staff, request, ct);
            department = await ResolveDepartmentForUpdateAsync(staff, hospital, request, ct);
            if (request.Facility is not null)
            {
                staff.HospitalId = hospital.Id;
                changedFields.Add(nameof(request.Facility));
            }

            if (request.Department is not null)
            {
                staff.DepartmentId = department.Id;
                changedFields.Add(nameof(request.Department));
            }
        }
        else
        {
            hospital = await LoadHospitalAsync(staff.HospitalId, ct);
            department = await LoadDepartmentAsync(staff.DepartmentId, ct);
        }

        var role = currentRoles[0];
        if (request.Role is not null)
        {
            if (currentRoles.Any(r =>
                    string.Equals(r.Code, SystemAdminRoleCode, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ForbiddenException("Không thể thay đổi vai trò của tài khoản quản trị hệ thống.");
            }

            role = await ResolveRoleAsync(request.Role, ct);
            _db.UserRoles.RemoveRange(assignments);
            _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            changedFields.Add(nameof(request.Role));
        }

        if (request.Status is not null ||
            request.Role is not null ||
            request.AuthProvider is not null ||
            request.Email is not null)
        {
            var activeSessions = await _db.AuthSessions
                .Where(session => session.UserId == user.Id && session.RevokedAt == null)
                .ToListAsync(ct);
            foreach (var session in activeSessions)
            {
                session.RevokedAt = nowUtc;
                session.RevokedByIp = ipAddress;
            }
        }

        await using var transaction = await _db.BeginTransactionAsync(ct);
        try
        {
            await _db.SaveChangesAsync(ct);

            _audit.Add(
                actorUserId,
                UpdatedAction,
                "Staff",
                staff.Id.ToString(),
                new
                {
                    staff.UserId,
                    staff.StaffCode,
                    ChangedFields = changedFields,
                    PreviousAuthProvider = previousAuthProvider,
                    user.AuthProvider,
                    PreviousStatus = previousStatus,
                    Status = GetEffectiveStatus(user, staff, nowUtc),
                    PreviousRoleCode = previousRoleCode,
                    RoleCode = role.Code,
                    PreviousHospitalId = previousHospitalId,
                    staff.HospitalId,
                    PreviousDepartmentId = previousDepartmentId,
                    staff.DepartmentId,
                },
                ipAddress);

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            throw new ConflictException(
                "Email hoặc dữ liệu tài khoản đã trùng với bản ghi hiện có.");
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        return new UpdateStaffAccountResponse(
            user.Id,
            staff.Id,
            staff.StaffCode,
            staff.FullName,
            user.Email,
            user.AuthProvider,
            GetEffectiveStatus(user, staff, nowUtc),
            role.Code,
            hospital?.Id,
            hospital?.Name,
            department?.Id,
            department?.Name,
            nowUtc);
    }

    private async Task EnsureSystemAdminAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var actor = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (actor.AccountStatus != AccountStatus.ACTIVE)
        {
            throw new ForbiddenException("Tài khoản quản trị hệ thống không ở trạng thái hoạt động.");
        }

        if (actor.LockedUntil.HasValue && actor.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản quản trị hệ thống đang bị tạm khóa.");
        }

        var hasSystemAdminRole = await (
            from assignment in _db.UserRoles.AsNoTracking()
            join role in _db.Roles.AsNoTracking() on assignment.RoleId equals role.Id
            where assignment.UserId == actorUserId && role.Code.ToUpper() == SystemAdminRoleCode
            select assignment.UserId).AnyAsync(ct);

        if (!hasSystemAdminRole)
        {
            throw new ForbiddenException("Người dùng không có quyền cập nhật tài khoản nhân sự.");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .Where(s => s.UserId == actorUserId)
            .Select(s => new { s.Status })
            .FirstOrDefaultAsync(ct);

        if (actorStaff is not null &&
            !string.Equals(actorStaff.Status, ActiveStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của quản trị viên đã ngừng hoạt động.");
        }
    }

    private async Task<Staff> FindStaffAsync(string staffIdentifier, CancellationToken ct)
    {
        var identifier = RequiredTrimmed(staffIdentifier, "Định danh nhân sự", 40);
        if (long.TryParse(identifier, out var staffId) && staffId > 0)
        {
            return await _db.Staff.FirstOrDefaultAsync(s => s.Id == staffId, ct)
                ?? throw new NotFoundException("Không tìm thấy tài khoản nhân sự.");
        }

        var staffCode = identifier.ToUpperInvariant();
        var matches = await _db.Staff
            .Where(s => s.StaffCode.ToUpper() == staffCode)
            .Take(2)
            .ToListAsync(ct);
        return matches.Count switch
        {
            0 => throw new NotFoundException("Không tìm thấy tài khoản nhân sự."),
            1 => matches[0],
            _ => throw new ConflictException(
                "Mã nhân sự tồn tại tại nhiều cơ sở; hãy dùng ID nhân sự."),
        };
    }

    private async Task<Hospital?> LoadHospitalAsync(long? hospitalId, CancellationToken ct)
    {
        if (hospitalId is null)
        {
            return null;
        }

        return await _db.Hospitals.AsNoTracking()
            .FirstOrDefaultAsync(hospital => hospital.Id == hospitalId.Value, ct);
    }

    private async Task<Department?> LoadDepartmentAsync(long? departmentId, CancellationToken ct)
    {
        if (departmentId is null)
        {
            return null;
        }

        return await _db.Departments.AsNoTracking()
            .FirstOrDefaultAsync(department => department.Id == departmentId.Value, ct);
    }

    private async Task<Hospital> ResolveHospitalForUpdateAsync(
        Staff staff,
        UpdateStaffAccountRequest request,
        CancellationToken ct)
    {
        if (request.Facility is null)
        {
            if (staff.HospitalId is null)
            {
                throw new ValidationException("Hồ sơ nhân sự chưa được gán cơ sở.");
            }

            var currentHospital = await _db.Hospitals.AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == staff.HospitalId.Value, ct)
                ?? throw new ValidationException("Cơ sở hiện tại của nhân sự không tồn tại.");
            if (request.Department is not null && currentHospital.IsActive != true)
            {
                throw new ValidationException("Cơ sở đã ngừng hoạt động.");
            }

            return currentHospital;
        }

        if (request.Department is null &&
            staff.HospitalId is not null)
        {
            var requestedHospital = await ResolveHospitalAsync(request.Facility, ct);
            if (requestedHospital.Id != staff.HospitalId.Value)
            {
                throw new ValidationException("Phải chọn khoa/phòng khi thay đổi cơ sở.");
            }

            return requestedHospital;
        }

        return await ResolveHospitalAsync(request.Facility, ct);
    }

    private async Task<Department> ResolveDepartmentForUpdateAsync(
        Staff staff,
        Hospital hospital,
        UpdateStaffAccountRequest request,
        CancellationToken ct)
    {
        if (request.Department is not null)
        {
            return await ResolveDepartmentAsync(hospital.Id, request.Department, ct);
        }

        if (staff.DepartmentId is null)
        {
            throw new ValidationException("Hồ sơ nhân sự chưa được gán khoa/phòng.");
        }

        var department = await _db.Departments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == staff.DepartmentId.Value, ct)
            ?? throw new ValidationException("Khoa/phòng hiện tại của nhân sự không tồn tại.");

        if (department.HospitalId != hospital.Id)
        {
            throw new ValidationException("Khoa/phòng không thuộc cơ sở đã chọn.");
        }
        if (request.Facility is not null && department.IsActive != true)
        {
            throw new ValidationException("Khoa/phòng đã ngừng hoạt động.");
        }


        return department;
    }

    private async Task<Hospital> ResolveHospitalAsync(string identifier, CancellationToken ct)
    {
        var normalized = RequiredTrimmed(identifier, "Cơ sở", 255).ToUpperInvariant();
        var codeMatches = await _db.Hospitals.AsNoTracking()
            .Where(hospital => hospital.HospitalCode.ToUpper() == normalized)
            .Take(2)
            .ToListAsync(ct);
        var hospital = codeMatches.Count switch
        {
            > 1 => throw new ValidationException("Mã cơ sở không xác định duy nhất."),
            1 => codeMatches[0],
            _ => await ResolveHospitalByNameAsync(normalized, ct),
        };

        if (hospital.IsActive != true)
        {
            throw new ValidationException("Cơ sở đã ngừng hoạt động.");
        }

        return hospital;
    }

    private async Task<Hospital> ResolveHospitalByNameAsync(
        string normalizedName,
        CancellationToken ct)
    {
        var matches = await _db.Hospitals.AsNoTracking()
            .Where(hospital => hospital.Name.ToUpper() == normalizedName)
            .Take(2)
            .ToListAsync(ct);
        return matches.Count switch
        {
            0 => throw new ValidationException("Cơ sở không tồn tại."),
            1 => matches[0],
            _ => throw new ValidationException(
                "Tên cơ sở không xác định duy nhất; hãy dùng mã cơ sở."),
        };
    }

    private async Task<Department> ResolveDepartmentAsync(
        long hospitalId,
        string identifier,
        CancellationToken ct)
    {
        var normalized = RequiredTrimmed(identifier, "Khoa/phòng", 200).ToUpperInvariant();
        var codeMatches = await _db.Departments.AsNoTracking()
            .Where(department =>
                department.HospitalId == hospitalId &&
                department.DepartmentCode.ToUpper() == normalized)
            .Take(2)
            .ToListAsync(ct);
        var department = codeMatches.Count switch
        {
            > 1 => throw new ValidationException("Mã khoa/phòng không xác định duy nhất."),
            1 => codeMatches[0],
            _ => await ResolveDepartmentByNameAsync(hospitalId, normalized, ct),
        };

        if (department.IsActive != true)
        {
            throw new ValidationException("Khoa/phòng đã ngừng hoạt động.");
        }

        return department;
    }

    private async Task<Department> ResolveDepartmentByNameAsync(
        long hospitalId,
        string normalizedName,
        CancellationToken ct)
    {
        var matches = await _db.Departments.AsNoTracking()
            .Where(department =>
                department.HospitalId == hospitalId &&
                department.Name.ToUpper() == normalizedName)
            .Take(2)
            .ToListAsync(ct);
        return matches.Count switch
        {
            0 => throw new ValidationException(
                "Khoa/phòng không tồn tại tại cơ sở đã chọn."),
            1 => matches[0],
            _ => throw new ValidationException(
                "Tên khoa/phòng không xác định duy nhất; hãy dùng mã khoa/phòng."),
        };
    }

    private async Task<Role> ResolveRoleAsync(string requestedRole, CancellationToken ct)
    {
        var roleCode = MapRoleCode(RequiredTrimmed(requestedRole, "Vai trò", 50).ToUpperInvariant());
        if (roleCode is PatientRoleCode or SystemAdminRoleCode)
        {
            throw new ValidationException($"Không thể gán vai trò '{roleCode}' cho tài khoản nhân sự.");
        }

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Code.ToUpper() == roleCode, ct)
            ?? throw new ValidationException($"Vai trò '{roleCode}' không tồn tại.");

        if (await IsRoleInactiveAsync(role, ct))
        {
            throw new ValidationException($"Vai trò '{role.Code}' đã ngừng hoạt động.");
        }

        return role;
    }

    private async Task<bool> IsRoleInactiveAsync(Role role, CancellationToken ct)
    {
        var roleId = role.Id.ToString();
        var payloads = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "Role" &&
                        (a.EntityId == roleId || a.EntityId == role.Code))
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Select(a => a.ChangeData)
            .ToListAsync(ct);

        foreach (var payload in payloads)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            try
            {
                var metadata = JsonSerializer.Deserialize<RoleStatusPayload>(payload, AuditJsonOptions);
                var status = metadata?.Status?.Trim();
                if (string.Equals(status, "INACTIVE", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(status, ActiveStatus, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            catch (JsonException)
            {
                // Older audit payloads may not be JSON; continue to the previous role event.
            }
        }

        return BaselineInactiveRoleCodes.Contains(role.Code);
    }

    private void ApplyAuthProvider(User user, string requestedProvider, string? password)
    {
        var provider = RequiredTrimmed(requestedProvider, "Phương thức đăng nhập", 20)
            .ToUpperInvariant();
        if (provider is not ("LOCAL" or "GOOGLE"))
        {
            throw new ValidationException("Phương thức đăng nhập chỉ hỗ trợ LOCAL hoặc GOOGLE.");
        }

        if (string.Equals(provider, user.AuthProvider, StringComparison.OrdinalIgnoreCase))
        {
            if (password is not null)
            {
                throw new ValidationException(
                    "Không thể đổi mật khẩu qua API cập nhật tài khoản nhân sự.");
            }

            user.AuthProvider = provider;
            return;
        }

        if (provider == "LOCAL")
        {
            if (!IsStrongPassword(password))
            {
                throw new ValidationException(
                    "Mật khẩu LOCAL phải có ít nhất 8 ký tự, gồm chữ hoa, chữ thường và số.");
            }

            user.AuthProvider = provider;
            user.PasswordHash = _passwordHasher.HashPassword(user, password!);
            return;
        }

        user.AuthProvider = provider;
        user.PasswordHash = null;
    }

    private static void ApplyStatus(
        User user,
        Staff staff,
        string requestedStatus,
        string previousStatus)
    {
        var status = RequiredTrimmed(requestedStatus, "Trạng thái", 16).ToUpperInvariant();
        if (previousStatus == DisabledStatus)
        {
            if (status != DisabledStatus)
            {
                throw new ValidationException(
                    "Tài khoản đã vô hiệu hóa chỉ được kích hoạt lại qua quy trình chuyên biệt.");
            }

            return;
        }

        switch (status)
        {
            case ActiveStatus:
                user.AccountStatus = AccountStatus.ACTIVE;
                user.LockedUntil = null;
                user.FailedLoginAttempts = 0;
                break;
            case LockedStatus:
                user.AccountStatus = AccountStatus.LOCKED;
                user.LockedUntil = null;
                break;
            case DisabledStatus:
                throw new ValidationException(
                    "Hãy dùng chức năng vô hiệu hóa tài khoản để kiểm tra dependency vận hành.");
            default:
                throw new ValidationException(
                    "Trạng thái tài khoản chỉ hỗ trợ ACTIVE, LOCKED hoặc DISABLED.");
        }

        if (!string.Equals(staff.Status, ActiveStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Hồ sơ nhân sự đã ngừng hoạt động.");
        }
    }

    private static string GetEffectiveStatus(User user, Staff staff, DateTime nowUtc)
    {
        if (user.AccountStatus == AccountStatus.INACTIVE ||
            !string.Equals(staff.Status, ActiveStatus, StringComparison.OrdinalIgnoreCase))
        {
            return DisabledStatus;
        }

        if (user.AccountStatus == AccountStatus.LOCKED ||
            (user.LockedUntil.HasValue && user.LockedUntil.Value > nowUtc))
        {
            return LockedStatus;
        }

        return user.AccountStatus.ToString();
    }

    private static void EnsureHasChanges(UpdateStaffAccountRequest request)
    {
        if (request.FullName is null &&
            request.AuthProvider is null &&
            request.Facility is null &&
            request.Email is null &&
            request.Password is null &&
            request.Status is null &&
            request.Role is null &&
            request.Department is null)
        {
            throw new ValidationException("Phải cung cấp ít nhất một trường cần cập nhật.");
        }
    }

    private static string NormalizeEmail(string email)
    {
        var normalized = RequiredTrimmed(email, "Email", 255).ToLowerInvariant();
        if (!EmailValidator.IsValid(normalized))
        {
            throw new ValidationException("Email không hợp lệ.");
        }

        return normalized;
    }

    private static string RequiredTrimmed(string? value, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException($"{fieldName} là bắt buộc.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ValidationException($"{fieldName} tối đa {maxLength} ký tự.");
        }

        return trimmed;
    }

    private static bool IsStrongPassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
        {
            return false;
        }

        var hasUpper = false;
        var hasLower = false;
        var hasDigit = false;
        foreach (var character in password)
        {
            hasUpper |= char.IsUpper(character);
            hasLower |= char.IsLower(character);
            hasDigit |= char.IsDigit(character);
        }

        return hasUpper && hasLower && hasDigit;
    }

    private static string MapRoleCode(string roleCode) => roleCode switch
    {
        "TECHNICIAN" => "DIAGNOSTIC_STAFF",
        "RECEPTIONIST" => "RECEPTION",
        _ => roleCode,
    };

    private sealed record RoleStatusPayload(string? Status);
}
