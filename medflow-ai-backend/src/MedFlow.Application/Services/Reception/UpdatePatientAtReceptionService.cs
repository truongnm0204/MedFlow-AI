using System.Text.RegularExpressions;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedFlow.Application.Services.Reception;

public sealed partial class UpdatePatientAtReceptionService : IUpdatePatientAtReceptionService
{
    private static readonly string[] AllowedGenders = ["MALE", "FEMALE", "OTHER"];

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<IdentityHashOptions>? _hashOptions;

    public UpdatePatientAtReceptionService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null,
        IOptions<IdentityHashOptions>? hashOptions = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _hashOptions = hashOptions;
    }

    public async Task<UpdatePatientResponseDto> UpdatePatientAsync(
        string idOrCode,
        UpdatePatientAtReceptionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(idOrCode))
            throw new ValidationException("Mã hoặc ID bệnh nhân là bắt buộc.");

        var key = idOrCode.Trim();
        var patientQuery = _db.Patients.AsQueryable();
        var patient = long.TryParse(key, out var id)
            ? await patientQuery.FirstOrDefaultAsync(p => p.Id == id, ct)
            : await patientQuery.FirstOrDefaultAsync(p => p.PatientCode == key, ct);

        if (patient is null)
            throw new NotFoundException("Patient", key);

        var fullName = request.FullName?.Trim() ?? string.Empty;
        if (fullName.Length is < 2 or > 200)
            throw new ValidationException("Họ tên phải từ 2 đến 200 ký tự.");

        if (request.DateOfBirth is { } dob && dob > HospitalClock.GetToday(_timeProvider))
            throw new ValidationException("Ngày sinh không được ở tương lai.");

        var gender = string.IsNullOrWhiteSpace(request.Gender) ? null : request.Gender.Trim().ToUpperInvariant();
        if (gender is not null && !AllowedGenders.Contains(gender))
            throw new ValidationException("Giới tính phải là MALE, FEMALE hoặc OTHER.");

        var phone = request.Phone?.Trim() ?? string.Empty;
        if (!PhoneRegex().IsMatch(phone))
            throw new ValidationException("Số điện thoại không hợp lệ.");

        var address = request.Address?.Trim() ?? string.Empty;
        if (address.Length == 0 || address.Length > 500)
            throw new ValidationException("Địa chỉ là bắt buộc và tối đa 500 ký tự.");

        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (email is not null && !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            throw new ValidationException("Email không hợp lệ.");

        string? rawIdentityCard = string.IsNullOrWhiteSpace(request.IdentityCard) ? null : request.IdentityCard.Trim();
        string? canonicalIdentityCard = null;
        string? hmacHash = null;
        string? legacyHash = null;
        var allowShaFallback = IdentificationNumber.AllowShaFallback(_hashOptions);

        if (rawIdentityCard is not null)
        {
            canonicalIdentityCard = IdentificationNumber.Normalize(rawIdentityCard);
            var hashKey = IdentificationNumber.RequireKey(_hashOptions);
            hmacHash = IdentificationNumber.ComputeHmac(canonicalIdentityCard, hashKey);
            if (allowShaFallback)
            {
                legacyHash = IdentificationNumber.ComputeSha256(canonicalIdentityCard);
            }

            var duplicate = await _db.Patients.AnyAsync(
                p => p.Id != patient.Id && p.IdentificationHashHmac == hmacHash,
                ct);
            if (!duplicate && allowShaFallback && legacyHash is not null)
            {
                duplicate = await _db.Patients.AnyAsync(
                    p => p.Id != patient.Id && p.IdentificationHash == legacyHash,
                    ct);
            }

            if (duplicate)
                throw new ConflictException("CCCD đã được đăng ký cho bệnh nhân khác.");
        }

        // PatientCode is immutable.
        patient.FullName = fullName;
        patient.DateOfBirth = request.DateOfBirth;
        patient.Gender = gender;
        patient.Phone = phone;
        patient.Address = address;
        patient.Email = email;
        patient.BloodType = string.IsNullOrWhiteSpace(request.BloodType) ? null : request.BloodType.Trim();
        if (hmacHash is not null)
        {
            patient.IdentificationHashHmac = hmacHash;
            patient.HashVersion = IdentificationNumber.CurrentHashVersion;
            if (allowShaFallback)
            {
                patient.IdentificationHash = legacyHash;
            }
        }

        await _db.SaveChangesAsync(ct);

        string? masked = canonicalIdentityCard is not null
            ? string.Concat(new string('*', Math.Max(4, canonicalIdentityCard.Length - 4)), canonicalIdentityCard.AsSpan(canonicalIdentityCard.Length - 4))
            : IdentificationNumber.HasVerifiedIdentity(patient, _hashOptions)
                ? PatientProfileService.MaskedVerifiedIdentityText
                : null;

        return new UpdatePatientResponseDto(
            patient.Id,
            patient.PatientCode,
            patient.FullName,
            patient.DateOfBirth,
            patient.Gender,
            patient.Phone,
            patient.Address,
            patient.Email,
            masked,
            patient.BloodType,
            HospitalClock.GetUtcNowUnspecified(_timeProvider));
    }

    [GeneratedRegex(@"^0[35789]\d{8}$")]
    private static partial Regex PhoneRegex();
}
