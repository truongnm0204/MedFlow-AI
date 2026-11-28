using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Ekyc;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedFlow.Application.Services.Ekyc;

/// <summary>
/// Owner-approved UNVERIFIED patient-profile creation (eKYC skipped by exception).
/// Rules enforced here, in order:
///   1. Staff must hold PATIENT_IDENTITY_VERIFY (service-layer authz).
///   2. Reason must be one of the owner-approved set; reasons implying a valid card require the
///      declared CCCD so dedupe can run.
///   3. Dedupe matrix runs FIRST: an existing profile with the same identification is a conflict
///      that reception must resolve (claim/link or data reconciliation) — never a silent duplicate.
///   4. Profile is written UNVERIFIED with reason + note, audited PATIENT_PROFILE_CREATED_UNVERIFIED.
/// There is NO code path here that can write VERIFIED.
/// </summary>
public sealed class ReceptionPatientProfileService : IReceptionPatientProfileService
{
    public const string ReasonRequiredMessage = "Vui lòng chọn lý do tạo hồ sơ chưa xác minh danh tính.";
    public const string ReasonInvalidMessage = "Lý do tạo hồ sơ chưa xác minh không hợp lệ.";
    public const string IdentityRequiredMessage =
        "Lý do này yêu cầu khai báo số CCCD/CMND để đối chiếu trùng lặp danh tính trước khi tạo hồ sơ.";
    public const string DuplicateIdentityMessage =
        "Số CCCD/CMND đã tồn tại trong hệ thống. Cần xử lý liên kết/xung đột danh tính trước khi tạo hồ sơ mới.";
    public const string FullNameRequiredMessage = "Họ tên là bắt buộc.";
    public const string DateOfBirthRequiredMessage = "Ngày sinh là bắt buộc.";
    public const string FutureDateOfBirthMessage = "Ngày sinh không được ở tương lai.";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly EkycOptions _ekycOptions;
    private readonly IOptions<IdentityHashOptions>? _identityOptions;

    public ReceptionPatientProfileService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null,
        IOptions<EkycOptions>? ekycOptions = null,
        IOptions<IdentityHashOptions>? identityOptions = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ekycOptions = ekycOptions?.Value ?? new EkycOptions();
        _identityOptions = identityOptions;
    }

    public async Task<CreateUnverifiedPatientProfileResponse> CreateUnverifiedAsync(
        long actorUserId,
        CreateUnverifiedPatientProfileRequest request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EkycStaffAuthority.RequireIdentityVerifyAsync(_db, actorUserId, _timeProvider, ct);

        var reason = request.Reason?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(ReasonRequiredMessage);
        }

        if (!PatientUnverifiedReasons.All.Contains(reason))
        {
            throw new ValidationException(ReasonInvalidMessage);
        }

        var fullName = request.FullName?.Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ValidationException(FullNameRequiredMessage);
        }

        if (fullName.Length > EkycConstants.DeclaredFullNameMaxLength)
        {
            throw new ValidationException(FullNameRequiredMessage);
        }

        if (request.DateOfBirth is null)
        {
            throw new ValidationException(DateOfBirthRequiredMessage);
        }

        var today = HospitalClock.GetToday(_timeProvider);
        if (request.DateOfBirth.Value > today)
        {
            throw new ValidationException(FutureDateOfBirthMessage);
        }

        var nowUtc = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var auditOccurredAt = _timeProvider.GetUtcNow().UtcDateTime;

        string? identityHmac = null;
        if (!string.IsNullOrWhiteSpace(request.IdentityCard))
        {
            var canonical = IdentificationNumber.Normalize(request.IdentityCard);
            var hmacKey = EkycCrypto.RequireHmacKey(
                Microsoft.Extensions.Options.Options.Create(_ekycOptions),
                _identityOptions);
            identityHmac = EkycCrypto.ComputeIdentificationHmac(canonical, hmacKey);
        }
        else if (PatientUnverifiedReasons.RequireDeclaredIdentification.Contains(reason))
        {
            throw new ValidationException(IdentityRequiredMessage);
        }

        // Dedupe matrix — done BEFORE creating anything. Existing identification means the intake
        // must resolve identity conflict/link first; no silent duplicate profile.
        if (identityHmac is not null)
        {
            var duplicateExists = await _db.Patients
                .AsNoTracking()
                .AnyAsync(p => p.IdentificationHashHmac == identityHmac, ct);
            if (duplicateExists)
            {
                throw new ConflictException(DuplicateIdentityMessage);
            }
        }

        var patient = new Patient
        {
            PatientCode = await GeneratePatientCodeAsync(today.Year, ct),
            FullName = fullName,
            DateOfBirth = request.DateOfBirth,
            Gender = string.IsNullOrWhiteSpace(request.Gender) ? null : request.Gender.Trim().ToUpperInvariant(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            IdentificationHashHmac = identityHmac,
            HashVersion = identityHmac is null ? null : IdentificationNumber.CurrentHashVersion,
            IdentityStatus = PatientIdentityStatuses.Unverified,
            UnverifiedReason = reason,
            IdentityNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            Status = "ACTIVE",
        };

        _db.Patients.Add(patient);

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = EkycConstants.AuditActionPatientProfileCreatedUnverified,
            EntityType = EkycConstants.AuditEntityTypePatient,
            // Entity id is generated on save; change_data carries the operational evidence.
            EntityId = null,
            Reason = "Tạo hồ sơ bệnh nhân chưa xác minh danh tính (ngoại lệ được duyệt).",
            ChangeData = System.Text.Json.JsonSerializer.Serialize(new
            {
                patientCode = patient.PatientCode,
                reason,
                note = patient.IdentityNote,
                identityStatus = PatientIdentityStatuses.Unverified,
                dedupeOutcome = identityHmac is null ? "NOT_APPLICABLE_NO_IDENTITY" : "NO_MATCH",
                performedBy = actorUserId,
            }),
            IpAddress = System.Net.IPAddress.TryParse(ipAddress, out var parsedIp) ? parsedIp : null,
            OccurredAt = auditOccurredAt,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException(DuplicateIdentityMessage);
        }

        return new CreateUnverifiedPatientProfileResponse(
            patient.Id,
            patient.PatientCode,
            patient.FullName,
            patient.IdentityStatus,
            patient.UnverifiedReason!);
    }

    /// <summary>Same BN-YYYY-NNNN scheme as the normal reception create path.</summary>
    private async Task<string> GeneratePatientCodeAsync(int year, CancellationToken ct)
    {
        var prefix = $"BN-{year}-";
        var lastCode = await _db.Patients
            .Where(p => p.PatientCode.StartsWith(prefix))
            .OrderByDescending(p => p.PatientCode.Length)
            .ThenByDescending(p => p.PatientCode)
            .Select(p => p.PatientCode)
            .FirstOrDefaultAsync(ct);

        var next = 1;
        if (lastCode is not null && int.TryParse(lastCode.AsSpan(prefix.Length), out var last))
        {
            next = last + 1;
        }

        var candidate = $"{prefix}{next:D4}";
        var exists = await _db.Patients.AnyAsync(p => p.PatientCode == candidate, ct);
        if (exists)
        {
            // Extremely narrow race — let the unique index decide; caller retries.
            candidate = $"{prefix}{next + 1:D4}";
        }

        return candidate;
    }
}
