using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Patients;

public sealed class PatientProfileService : IPatientProfileService
{
    private const string MaskedVerifiedIdentityText = "****** (Đã xác thực CCCD)";

    private static readonly IReadOnlyList<string> DefaultAllowedActions =
    [
        "UPDATE_PERSONAL_PROFILE",
        "MANAGE_FAMILY_PROFILES",
        "DOWNLOAD_PROFILE_SUMMARY",
    ];

    private readonly IApplicationDbContext _db;

    public PatientProfileService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PatientProfileResponse> GetPersonalProfileAsync(
        long userId,
        CancellationToken ct = default)
    {
        if (userId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        var patient = await _db.Patients
            .AsNoTracking()
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (patient is null)
            throw new NotFoundException("Patient", userId);

        if (patient.User is null || patient.User.AccountStatus != AccountStatus.ACTIVE)
            throw new UnauthorizedAccessException("Tài khoản bệnh nhân chưa kích hoạt hoặc đã bị khóa.");

        if (!string.Equals(patient.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Hồ sơ bệnh nhân đang ngừng hoạt động.");

        var email = !string.IsNullOrWhiteSpace(patient.Email)
            ? patient.Email
            : patient.User.Email;

        var isEmailVerified = patient.User.EmailVerifiedAt.HasValue;
        var isIdentityVerified = !string.IsNullOrWhiteSpace(patient.IdentificationHash);
        var maskedIdentification = isIdentityVerified ? MaskedVerifiedIdentityText : null;
        var age = CalculateAge(patient.DateOfBirth);

        return new PatientProfileResponse(
            Id: patient.Id,
            UserId: userId,
            PatientCode: patient.PatientCode,
            FullName: patient.FullName,
            DateOfBirth: patient.DateOfBirth,
            Age: age,
            Gender: patient.Gender,
            Phone: patient.Phone,
            Email: email,
            IsEmailVerified: isEmailVerified,
            IsIdentityVerified: isIdentityVerified,
            MaskedIdentification: maskedIdentification,
            Status: patient.Status,
            Address: patient.Address,
            BloodType: patient.BloodType,
            AllowedActions: DefaultAllowedActions
        );
    }

    private static int? CalculateAge(DateOnly? dateOfBirth)
    {
        if (!dateOfBirth.HasValue)
            return null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dob = dateOfBirth.Value;
        var age = today.Year - dob.Year;

        if (dob > today.AddYears(-age))
            age--;

        return Math.Max(0, age);
    }
}
