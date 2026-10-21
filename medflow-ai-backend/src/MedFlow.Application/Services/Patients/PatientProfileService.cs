using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Patients;

public sealed class PatientProfileService : IPatientProfileService
{
    private readonly IApplicationDbContext _db;

    public PatientProfileService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PatientProfileResponse> UpdatePersonalProfileAsync(
        Guid userId,
        UpdatePersonalProfileRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Kiểm tra quyền sở hữu (GBR-PAT-01): Tìm hồ sơ Patient gắn với tài khoản đang đăng nhập
        var patient = await _db.Patients
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (patient == null)
        {
            throw new NotFoundException("Patient", userId);
        }

        // 2. Kiểm tra trùng số CCCD/CMND (GBR-PAT-02): Không được trùng với bệnh nhân khác
        if (!string.IsNullOrWhiteSpace(request.IdentificationNo))
        {
            var isDuplicateId = await _db.Patients
                .AnyAsync(p => p.Id != patient.Id && p.IdentificationNo == request.IdentificationNo, ct);

            if (isDuplicateId)
            {
                throw new ValidationException("Số CMND/CCCD này đã được sử dụng bởi một bệnh nhân khác trong hệ thống.");
            }
        }

        // 3. Cập nhật các trường thông tin được phép thay đổi
        if (!string.IsNullOrWhiteSpace(request.FullName))
            patient.FullName = request.FullName.Trim();

        patient.Phone = request.Phone?.Trim();
        patient.Email = request.Email?.Trim();
        patient.DateOfBirth = request.DateOfBirth;
        patient.Gender = request.Gender?.Trim();
        patient.IdentificationNo = request.IdentificationNo?.Trim();
        patient.IdentityIssuedDate = request.IdentityIssuedDate;
        patient.IdentityIssuedPlace = request.IdentityIssuedPlace?.Trim();
        patient.Address = request.Address?.Trim();
        patient.AvatarUrl = request.AvatarUrl?.Trim();
        patient.Nationality = request.Nationality?.Trim();
        patient.BloodType = request.BloodType?.Trim();
        patient.EmergencyContactName = request.EmergencyContactName?.Trim();
        patient.EmergencyContactPhone = request.EmergencyContactPhone?.Trim();
        patient.EmergencyContactRelationship = request.EmergencyContactRelationship?.Trim();
        patient.UpdatedAt = DateTime.UtcNow;

        // 4. Lưu thay đổi vào Database
        await _db.SaveChangesAsync(ct);

        // 5. Trả về DTO kết quả
        return new PatientProfileResponse(
            patient.Id,
            patient.PatientCode,
            patient.FullName,
            patient.Phone,
            patient.Email,
            patient.DateOfBirth,
            patient.Gender,
            patient.IdentificationNo,
            patient.IdentityIssuedDate,
            patient.IdentityIssuedPlace,
            patient.Address,
            patient.AvatarUrl,
            patient.Nationality,
            patient.BloodType,
            patient.EmergencyContactName,
            patient.EmergencyContactPhone,
            patient.EmergencyContactRelationship,
            patient.UpdatedAt
        );
    }
}
