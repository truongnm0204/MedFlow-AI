using System.ComponentModel.DataAnnotations;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using DomainValidationException = MedFlow.Domain.Exceptions.ValidationException;
using DomainConflictException = MedFlow.Domain.Exceptions.ConflictException;

namespace MedFlow.Application.Services.Reception;

public sealed class CreatePatientAtReceptionService : ICreatePatientAtReceptionService
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _time;

    public CreatePatientAtReceptionService(IApplicationDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<CreatePatientResponseDto> CreatePatientAsync(
        CreatePatientAtReceptionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        var fullName = request.FullName?.Trim() ?? string.Empty;
        var address = request.Address?.Trim() ?? string.Empty;
        var normalized = new CreatePatientAtReceptionRequest
        {
            FullName = fullName,
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender?.Trim() ?? string.Empty,
            Phone = request.Phone?.Trim() ?? string.Empty,
            Address = address,
            IdentityCard = NullIfBlank(request.IdentityCard),
            Email = NullIfBlank(request.Email),
            BloodType = NullIfBlank(request.BloodType),
        };

        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalized, new ValidationContext(normalized), results, validateAllProperties: true))
        {
            throw new DomainValidationException(string.Join("; ", results.Select(r => r.ErrorMessage)));
        }

        if (normalized.DateOfBirth!.Value > today)
        {
            throw new DomainValidationException("Ngày sinh không được ở tương lai");
        }

        string? hash = null;
        string? masked = null;
        if (normalized.IdentityCard is not null)
        {
            var canonicalId = IdentificationNumber.Normalize(normalized.IdentityCard);
            hash = IdentificationNumber.ComputeSha256(canonicalId);
            if (await _db.Patients.AnyAsync(p => p.IdentificationHash == hash, ct))
            {
                throw new DomainConflictException("CCCD đã tồn tại trong hệ thống");
            }

            masked = new string('*', 8) + canonicalId[^4..];
        }

        var prefix = $"BN-{now.Year}-";
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

        var patient = new Patient
        {
            PatientCode = $"{prefix}{next:D4}",
            FullName = fullName,
            DateOfBirth = normalized.DateOfBirth,
            Gender = normalized.Gender.ToUpperInvariant(),
            Phone = normalized.Phone,
            Email = normalized.Email,
            IdentificationHash = hash,
            Address = address,
            BloodType = normalized.BloodType?.ToUpperInvariant(),
            Status = "ACTIVE",
        };

        _db.Patients.Add(patient);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Unique index violation on patient_code (concurrent creation) — let client retry.
            throw new DomainConflictException("Không thể tạo mã bệnh nhân duy nhất, vui lòng thử lại");
        }

        return new CreatePatientResponseDto(
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
            now);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
