using EmailAddressAttribute = System.ComponentModel.DataAnnotations.EmailAddressAttribute;
using System.Text.Json;
using System.Text.RegularExpressions;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.SystemAdmin;

public sealed partial class StaffAccountService : IStaffAccountService
{
    private const string SystemAdminRoleCode = "SYSTEM_ADMIN";
    private const string PatientRoleCode = "PATIENT";
    private const string CreatedAction = "STAFF_ACCOUNT_CREATED";

    private static readonly EmailAddressAttribute EmailValidator = new();
    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IApplicationDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly TimeProvider _timeProvider;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public StaffAccountService(
        IApplicationDbContext db,
        IAuditLogger audit,
        TimeProvider timeProvider)
    {
        _db = db;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    public async Task<CreateStaffAccountResponse> CreateAsync(
        long actorUserId,
        CreateStaffAccountRequest request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await EnsureSystemAdminAccessAsync(actorUserId, nowUtc, ct);

        var input = ValidateAndNormalize(request);
        var hospital = await ResolveHospitalAsync(input.Facility, ct);
        var department = await ResolveDepartmentAsync(hospital.Id, input.Department, ct);
        var role = await ResolveRoleAsync(input.RoleCode, ct);

        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == input.Email, ct))
        {
            throw new ConflictException($"Email '{input.Email}' đã được sử dụng.");
        }

        if (await _db.Staff.AnyAsync(
                s => s.HospitalId == hospital.Id && s.StaffCode.ToUpper() == input.StaffCode,
                ct))
        {
            throw new ConflictException(
                $"Mã nhân sự '{input.StaffCode}' đã tồn tại tại cơ sở đã chọn.");
        }

        var user = new User
        {
            Email = input.Email,
            AuthProvider = input.AuthProvider,
            AccountStatus = input.Status,
            EmailVerifiedAt = nowUtc,
        };

        if (input.AuthProvider == "LOCAL")
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, input.Password!);
        }

        var staff = new Staff
        {
            User = user,
            HospitalId = hospital.Id,
            DepartmentId = department.Id,
            StaffCode = input.StaffCode,
            FullName = input.FullName,
            Status = "ACTIVE",
        };

        user.UserRoles.Add(new UserRole { User = user, Role = role });

        await using var transaction = await _db.BeginTransactionAsync(ct);
        try
        {
            _db.Users.Add(user);
            _db.Staff.Add(staff);
            await _db.SaveChangesAsync(ct);

            _audit.Add(
                actorUserId,
                CreatedAction,
                "Staff",
                staff.Id.ToString(),
                new
                {
                    staff.UserId,
                    staff.StaffCode,
                    user.AuthProvider,
                    AccountStatus = user.AccountStatus.ToString(),
                    RoleCode = role.Code,
                    HospitalId = hospital.Id,
                    DepartmentId = department.Id,
                },
                ipAddress);

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            throw new ConflictException(
                "Email hoặc mã nhân sự đã tồn tại; không thể tạo tài khoản trùng lặp.");
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        return new CreateStaffAccountResponse(
            user.Id,
            staff.Id,
            staff.StaffCode,
            staff.FullName,
            user.Email,
            user.AuthProvider,
            user.AccountStatus.ToString(),
            role.Code,
            hospital.Id,
            hospital.Name,
            department.Id,
            department.Name,
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

        var roleIds = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var hasSystemAdminRole = await _db.Roles
            .AsNoTracking()
            .AnyAsync(
                r => roleIds.Contains(r.Id) && r.Code.ToUpper() == SystemAdminRoleCode,
                ct);

        if (!hasSystemAdminRole)
        {
            throw new ForbiddenException("Người dùng không có quyền tạo tài khoản nhân sự.");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .Where(s => s.UserId == actorUserId)
            .Select(s => new { s.Status })
            .FirstOrDefaultAsync(ct);

        if (actorStaff is not null &&
            !string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của quản trị viên đã ngừng hoạt động.");
        }
    }

    private async Task<Hospital> ResolveHospitalAsync(string identifier, CancellationToken ct)
    {
        var hospital = await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(
                h => h.HospitalCode.ToUpper() == identifier || h.Name.ToUpper() == identifier,
                ct)
            ?? throw new ValidationException("Cơ sở không tồn tại.");

        if (hospital.IsActive != true)
        {
            throw new ValidationException("Cơ sở đã ngừng hoạt động.");
        }

        return hospital;
    }

    private async Task<Department> ResolveDepartmentAsync(
        long hospitalId,
        string identifier,
        CancellationToken ct)
    {
        var department = await _db.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.HospitalId == hospitalId &&
                     (d.DepartmentCode.ToUpper() == identifier || d.Name.ToUpper() == identifier),
                ct)
            ?? throw new ValidationException("Khoa/phòng không tồn tại tại cơ sở đã chọn.");

        if (department.IsActive != true)
        {
            throw new ValidationException("Khoa/phòng đã ngừng hoạt động.");
        }

        return department;
    }

    private async Task<Role> ResolveRoleAsync(string roleCode, CancellationToken ct)
    {
        var role = await _db.Roles
            .FirstOrDefaultAsync(r => r.Code.ToUpper() == roleCode, ct)
            ?? throw new ValidationException($"Vai trò '{roleCode}' không tồn tại.");

        if (string.Equals(role.Code, PatientRoleCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Không thể gán vai trò PATIENT cho tài khoản nhân sự.");
        }

        if (await IsRoleInactiveAsync(role, ct))
        {
            throw new ValidationException($"Vai trò '{role.Code}' đã ngừng hoạt động.");
        }

        return role;
    }

    private async Task<bool> IsRoleInactiveAsync(Role role, CancellationToken ct)
    {
        var roleId = role.Id.ToString();
        var payloads = await _db.AuditLogs
            .AsNoTracking()
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

                if (string.Equals(status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            catch (JsonException)
            {
                // Older audit payloads may not be JSON; continue to the previous role event.
            }
        }

        return string.Equals(
            RoleCatalogService.GetBaselineStatus(role.Code),
            "INACTIVE",
            StringComparison.OrdinalIgnoreCase);
    }

    private static NormalizedInput ValidateAndNormalize(CreateStaffAccountRequest request)
    {
        var staffCode = RequiredUpper(request.StaffCode, "Mã nhân sự");
        if (!StaffCodeRegex().IsMatch(staffCode))
        {
            throw new ValidationException("Mã nhân sự phải theo định dạng STF-xxxx.");
        }

        var fullName = RequiredTrimmed(request.FullName, "Họ tên");
        if (fullName.Length > 200)
        {
            throw new ValidationException("Họ tên tối đa 200 ký tự.");
        }

        var email = RequiredTrimmed(request.Email, "Email").ToLowerInvariant();
        if (email.Length > 255 || !EmailValidator.IsValid(email))
        {
            throw new ValidationException("Email không hợp lệ.");
        }

        var authProvider = RequiredUpper(request.AuthProvider, "Nhà cung cấp đăng nhập");
        if (authProvider is not ("LOCAL" or "GOOGLE"))
        {
            throw new ValidationException("Nhà cung cấp đăng nhập chỉ hỗ trợ LOCAL hoặc GOOGLE.");
        }

        var password = request.Password;
        if (authProvider == "LOCAL" && !IsStrongPassword(password))
        {
            throw new ValidationException(
                "Mật khẩu LOCAL phải có ít nhất 8 ký tự, gồm chữ hoa, chữ thường và số.");
        }

        var statusText = RequiredUpper(request.Status, "Trạng thái");
        var status = statusText switch
        {
            "ACTIVE" => AccountStatus.ACTIVE,
            "LOCKED" => AccountStatus.LOCKED,
            _ => throw new ValidationException(
                "Trạng thái tài khoản chỉ hỗ trợ ACTIVE hoặc LOCKED."),
        };

        var roleCode = MapRoleCode(RequiredUpper(request.Role, "Vai trò"));
        var facility = RequiredUpper(request.Facility, "Cơ sở");
        var department = RequiredUpper(request.Department, "Khoa/phòng");

        return new NormalizedInput(
            staffCode,
            fullName,
            authProvider,
            facility,
            email,
            password,
            status,
            roleCode,
            department);
    }

    private static string MapRoleCode(string roleCode) => roleCode switch
    {
        "TECHNICIAN" => "DIAGNOSTIC_STAFF",
        "RECEPTIONIST" => "RECEPTION",
        _ => roleCode,
    };

    private static string RequiredTrimmed(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException($"{fieldName} là bắt buộc.");
        }

        return value.Trim();
    }

    private static string RequiredUpper(string? value, string fieldName) =>
        RequiredTrimmed(value, fieldName).ToUpperInvariant();

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

    [GeneratedRegex("^STF-[0-9]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex StaffCodeRegex();

    private sealed record RoleStatusPayload(string? Status);

    private sealed record NormalizedInput(
        string StaffCode,
        string FullName,
        string AuthProvider,
        string Facility,
        string Email,
        string? Password,
        AccountStatus Status,
        string RoleCode,
        string Department);
}
