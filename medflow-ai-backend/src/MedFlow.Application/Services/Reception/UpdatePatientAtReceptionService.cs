using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Reception;

public sealed partial class UpdatePatientAtReceptionService : IUpdatePatientAtReceptionService
{
    private static readonly string[] AllowedGenders = ["MALE", "FEMALE", "OTHER"];

    private readonly IApplicationDbContext _db;

    public UpdatePatientAtReceptionService(IApplicationDbContext db)
    {
        _db = db;
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

        if (request.DateOfBirth is { } dob && dob > DateOnly.FromDateTime(DateTime.UtcNow))
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

        string? identityCard = string.IsNullOrWhiteSpace(request.IdentityCard) ? null : request.IdentityCard.Trim();
        string? hash = null;
        if (identityCard is not null)
        {
            if (!IdentityCardRegex().IsMatch(identityCard))
                throw new ValidationException("CCCD phải gồm đúng 12 chữ số.");

            hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identityCard))).ToLowerInvariant();
            var duplicate = await _db.Patients.AnyAsync(p => p.Id != patient.Id && p.IdentificationHash == hash, ct);
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
        if (hash is not null)
            patient.IdentificationHash = hash;

        await _db.SaveChangesAsync(ct);

        string? masked = identityCard is not null
            ? string.Concat("********", identityCard.AsSpan(8))
            : !string.IsNullOrWhiteSpace(patient.IdentificationHash)
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
            DateTime.UtcNow);
    }

    [GeneratedRegex(@"^0[35789]\d{8}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^\d{12}$")]
    private static partial Regex IdentityCardRegex();
}
