using System.Globalization;
using System.Text.RegularExpressions;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace MedFlow.Application.Services.Patients;

public sealed partial class PatientProfileService : IPatientProfileService
{
    internal const string MaskedVerifiedIdentityText = "****** (Đã xác thực CCCD)";
    public const string ProfileUpdatedAuditAction = "PATIENT_PROFILE_UPDATED";

    private static readonly HashSet<string> AllowedGenders = new(StringComparer.OrdinalIgnoreCase)
    {
        "MALE",
        "FEMALE",
        "OTHER",
    };

    private static readonly HashSet<string> AllowedBloodTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "A+",
        "A-",
        "B+",
        "B-",
        "AB+",
        "AB-",
        "O+",
        "O-",
    };

    private static readonly IReadOnlyList<string> DefaultAllowedActions =
    [
        "UPDATE_PERSONAL_PROFILE",
        "MANAGE_FAMILY_PROFILES",
        "DOWNLOAD_PROFILE_SUMMARY",
    ];

    private readonly IApplicationDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<IdentityHashOptions>? _hashOptions;

    public PatientProfileService(
        IApplicationDbContext db,
        IAuditLogger audit,
        TimeProvider? timeProvider = null,
        IOptions<IdentityHashOptions>? hashOptions = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(audit);

        _db = db;
        _audit = audit;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _hashOptions = hashOptions;
    }

    public async Task<PatientProfileResponse> GetPersonalProfileAsync(
        long userId,
        CancellationToken ct = default)
    {
        if (userId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        var patient = await PatientSelfResolver.LoadActiveAsync(_db, userId, ct, _timeProvider);
        var user = patient.User
            ?? throw new UnauthorizedAccessException("Tài khoản bệnh nhân chưa kích hoạt hoặc đã bị khóa.");

        return MapToResponse(patient, user, userId);
    }

    public async Task<PatientProfileResponse> UpdatePersonalProfileAsync(
        long userId,
        UpdateMyPatientProfileRequest request,
        bool isPatch = false,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (userId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        ArgumentNullException.ThrowIfNull(request);

        await using var tx = await _db.BeginTransactionAsync(ct);

        var patient = await PatientSelfResolver.LoadActiveTrackedAsync(_db, userId, ct, _timeProvider);
        var user = patient.User
            ?? throw new UnauthorizedAccessException("Tài khoản bệnh nhân chưa kích hoạt hoặc đã bị khóa.");

        await _db.TryLockPatientForUpdateAsync(patient.Id, ct);

        EnsureLegalIdentityNotTampered(request, patient);

        var isIdentityVerified = IdentificationNumber.HasVerifiedIdentity(patient, _hashOptions);
        var today = HospitalClock.GetToday(_timeProvider);
        var changedFields = new List<string>();
        var selfReportedBloodTypeUpdated = false;

        if (request.DateOfBirth.IsSpecified)
        {
            if (isIdentityVerified)
            {
                if (request.DateOfBirth.Value != patient.DateOfBirth)
                    throw new ValidationException("Hồ sơ đã xác thực CCCD, không thể tự thay đổi ngày sinh.");
            }
            else if (request.DateOfBirth.Value is { } nextDob)
            {
                if (nextDob > today || nextDob < new DateOnly(1900, 1, 1))
                    throw new ValidationException("Ngày sinh không hợp lệ.");

                if (patient.DateOfBirth != nextDob)
                {
                    patient.DateOfBirth = nextDob;
                    changedFields.Add("dateOfBirth");
                }
            }
            else if (patient.DateOfBirth.HasValue)
            {
                throw new ValidationException("Không thể xóa trắng ngày sinh đã khai báo.");
            }
        }

        if (request.Gender.IsSpecified)
        {
            var nextGender = TrimOrNull(request.Gender.Value)?.ToUpperInvariant();
            if (nextGender is not null && !AllowedGenders.Contains(nextGender))
                throw new ValidationException("Giới tính phải là MALE, FEMALE hoặc OTHER.");

            if (isIdentityVerified)
            {
                if (!string.Equals(nextGender, patient.Gender, StringComparison.OrdinalIgnoreCase))
                    throw new ValidationException("Hồ sơ đã xác thực CCCD, không thể tự thay đổi giới tính.");
            }
            else if (nextGender is not null)
            {
                if (!string.Equals(patient.Gender, nextGender, StringComparison.OrdinalIgnoreCase))
                {
                    patient.Gender = nextGender;
                    changedFields.Add("gender");
                }
            }
            else if (!string.IsNullOrWhiteSpace(patient.Gender))
            {
                throw new ValidationException("Không thể xóa trắng giới tính đã khai báo.");
            }
        }

        if (request.Phone.IsSpecified || !isPatch)
        {
            var nextPhone = request.Phone.IsSpecified ? TrimOrNull(request.Phone.Value) : null;
            if (string.IsNullOrEmpty(nextPhone) || !VietnamPhoneRegex().IsMatch(nextPhone))
                throw new ValidationException("Số điện thoại không hợp lệ.");

            if (!string.Equals(patient.Phone, nextPhone, StringComparison.Ordinal))
            {
                patient.Phone = nextPhone;
                changedFields.Add("phone");
            }
        }

        if (request.Address.IsSpecified || !isPatch)
        {
            var nextAddress = request.Address.IsSpecified ? TrimOrNull(request.Address.Value) : null;
            if (nextAddress is { Length: > 500 })
                throw new ValidationException("Địa chỉ tối đa 500 ký tự.");

            if (!string.Equals(patient.Address, nextAddress, StringComparison.Ordinal))
            {
                patient.Address = nextAddress;
                changedFields.Add("address");
            }
        }

        if (request.BloodType.IsSpecified || !isPatch)
        {
            if (isPatch && !request.BloodType.IsSpecified)
            {
                // Preserve existing BloodType when omitted on PATCH.
            }
            else
            {
                var nextBloodType = request.BloodType.IsSpecified
                    ? TrimOrNull(request.BloodType.Value)?.ToUpperInvariant()
                    : null;

                if (nextBloodType is not null && !AllowedBloodTypes.Contains(nextBloodType))
                {
                    throw new ValidationException(
                        "Nhóm máu không hợp lệ. Giá trị hợp lệ: A+, A-, B+, B-, AB+, AB-, O+, O-.");
                }

                if (!string.Equals(patient.BloodType, nextBloodType, StringComparison.OrdinalIgnoreCase))
                {
                    patient.BloodType = nextBloodType;
                    changedFields.Add("bloodType");
                    selfReportedBloodTypeUpdated = nextBloodType is not null;
                }
            }
        }

        if (request.EmergencyContact.IsSpecified || !isPatch)
        {
            ApplyEmergencyContactUpdate(request, isPatch, patient, changedFields);
        }

        _audit.Add(
            userId,
            ProfileUpdatedAuditAction,
            "PATIENT",
            patient.Id.ToString(CultureInfo.InvariantCulture),
            new
            {
                patient.PatientCode,
                Method = isPatch ? "PATCH" : "PUT",
                ChangedFields = changedFields,
                BloodTypeSource = selfReportedBloodTypeUpdated ? "PATIENT_SELF_REPORTED" : null,
                HasEmergencyContact = !string.IsNullOrWhiteSpace(patient.EmergencyContactName),
            },
            ipAddress);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return MapToResponse(patient, user, userId);
    }

    private void EnsureLegalIdentityNotTampered(
        UpdateMyPatientProfileRequest request,
        Patient patient)
    {
        var hasVerifiedIdentity = IdentificationNumber.HasVerifiedIdentity(patient, _hashOptions);
        if ((request.IdentityCard.IsSpecified && (hasVerifiedIdentity || !string.IsNullOrWhiteSpace(request.IdentityCard.Value))) ||
            (request.IdentificationHash.IsSpecified && (hasVerifiedIdentity || !string.IsNullOrWhiteSpace(request.IdentificationHash.Value))))
        {
            throw new ValidationException(
                "Số CCCD / định danh pháp lý không thể tự chỉnh sửa trực tuyến. Vui lòng đối chiếu tại quầy tiếp đón.");
        }

        if (request.FullName.IsSpecified)
        {
            var candidateName = TrimOrNull(request.FullName.Value);
            if (candidateName is null || !string.Equals(candidateName, patient.FullName, StringComparison.Ordinal))
            {
                throw new ValidationException(
                    "Họ và tên định danh pháp lý được khóa cố định và không thể tự chỉnh sửa.");
            }
        }
    }

    private static void ApplyEmergencyContactUpdate(
        UpdateMyPatientProfileRequest request,
        bool isPatch,
        Patient patient,
        List<string> changedFields)
    {
        var ec = request.EmergencyContact.IsSpecified ? request.EmergencyContact.Value : null;
        if (ec is null)
        {
            if (patient.EmergencyContactName is not null ||
                patient.EmergencyContactRelationship is not null ||
                patient.EmergencyContactPhone is not null)
            {
                patient.EmergencyContactName = null;
                patient.EmergencyContactRelationship = null;
                patient.EmergencyContactPhone = null;
                changedFields.Add("emergencyContact");
            }

            return;
        }

        var nextName = ec.FullName.IsSpecified
            ? TrimOrNull(ec.FullName.Value)
            : (isPatch ? patient.EmergencyContactName : null);
        var nextRelationship = ec.Relationship.IsSpecified
            ? TrimOrNull(ec.Relationship.Value)
            : (isPatch ? patient.EmergencyContactRelationship : null);
        var nextPhone = ec.Phone.IsSpecified
            ? TrimOrNull(ec.Phone.Value)
            : (isPatch ? patient.EmergencyContactPhone : null);

        if (nextName is null && nextRelationship is null && nextPhone is null)
        {
            if (patient.EmergencyContactName is not null ||
                patient.EmergencyContactRelationship is not null ||
                patient.EmergencyContactPhone is not null)
            {
                patient.EmergencyContactName = null;
                patient.EmergencyContactRelationship = null;
                patient.EmergencyContactPhone = null;
                changedFields.Add("emergencyContact");
            }

            return;
        }

        if (string.IsNullOrEmpty(nextName) || nextName.Length is < 2 or > 200)
            throw new ValidationException("Họ tên người liên hệ khẩn cấp phải từ 2 đến 200 ký tự.");

        if (string.IsNullOrEmpty(nextRelationship) || nextRelationship.Length > 60)
            throw new ValidationException("Mối quan hệ người liên hệ khẩn cấp là bắt buộc và tối đa 60 ký tự.");

        if (string.IsNullOrEmpty(nextPhone) || !VietnamPhoneRegex().IsMatch(nextPhone))
            throw new ValidationException("Số điện thoại người liên hệ khẩn cấp không hợp lệ.");

        if (!string.Equals(patient.EmergencyContactName, nextName, StringComparison.Ordinal) ||
            !string.Equals(patient.EmergencyContactRelationship, nextRelationship, StringComparison.Ordinal) ||
            !string.Equals(patient.EmergencyContactPhone, nextPhone, StringComparison.Ordinal))
        {
            patient.EmergencyContactName = nextName;
            patient.EmergencyContactRelationship = nextRelationship;
            patient.EmergencyContactPhone = nextPhone;
            changedFields.Add("emergencyContact");
        }
    }

    private PatientProfileResponse MapToResponse(Patient patient, User user, long userId)
    {
        var email = !string.IsNullOrWhiteSpace(patient.Email)
            ? patient.Email
            : user.Email;

        var isEmailVerified = user.EmailVerifiedAt.HasValue;
        var isIdentityVerified = IdentificationNumber.HasVerifiedIdentity(patient, _hashOptions);
        var maskedIdentification = isIdentityVerified ? MaskedVerifiedIdentityText : null;
        var age = CalculateAge(patient.DateOfBirth, HospitalClock.GetToday(_timeProvider));

        PatientEmergencyContactInfoDto? emergencyContact =
            !string.IsNullOrWhiteSpace(patient.EmergencyContactName) ||
            !string.IsNullOrWhiteSpace(patient.EmergencyContactRelationship) ||
            !string.IsNullOrWhiteSpace(patient.EmergencyContactPhone)
                ? new PatientEmergencyContactInfoDto(
                    patient.EmergencyContactName,
                    patient.EmergencyContactRelationship,
                    patient.EmergencyContactPhone)
                : null;

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
            AllowedActions: DefaultAllowedActions,
            EmergencyContact: emergencyContact
        );
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? CalculateAge(DateOnly? dateOfBirth, DateOnly today)
    {
        if (!dateOfBirth.HasValue)
            return null;

        var dob = dateOfBirth.Value;
        var age = today.Year - dob.Year;

        if (dob > today.AddYears(-age))
            age--;

        return Math.Max(0, age);
    }

    [GeneratedRegex(@"^0[35789]\d{8}$")]
    private static partial Regex VietnamPhoneRegex();
}
