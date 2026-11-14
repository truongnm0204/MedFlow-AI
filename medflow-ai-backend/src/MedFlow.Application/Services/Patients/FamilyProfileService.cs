using System.Globalization;
using System.Text.RegularExpressions;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedFlow.Application.Services.Patients;

public sealed class FamilyProfileService : IFamilyProfileService
{
    private const string MaskedVerifiedText = "****** (Đã xác thực CCCD)";
    private const string UnverifiedIdentityText = "Chưa cập nhật CCCD";

    private readonly IApplicationDbContext _db;
    private readonly IOptions<IdentityHashOptions>? _hashOptions;

    public FamilyProfileService(
        IApplicationDbContext db,
        IOptions<IdentityHashOptions>? hashOptions = null)
    {
        _db = db;
        _hashOptions = hashOptions;
    }

    public async Task<IReadOnlyList<FamilyMemberResponse>> GetFamilyMembersAsync(
        long userId,
        bool includeSelf = true,
        CancellationToken ct = default)
    {
        var self = await LoadActiveSelfAsync(userId, ct);

        var links = await _db.PatientFamilyLinks
            .AsNoTracking()
            .Include(l => l.MemberPatient)
            .Where(l => l.OwnerPatientId == self.Id && l.RevokedAt == null)
            .OrderBy(l => l.MemberPatientId)
            .ToListAsync(ct);

        var result = new List<FamilyMemberResponse>();

        if (includeSelf)
        {
            result.Add(BuildSelfResponse(self));
        }

        foreach (var link in links)
        {
            var member = link.MemberPatient;
            if (member is null || !string.Equals(member.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(BuildMemberResponse(self, member, link, 0, false, null));
        }

        return result;
    }

    public async Task<FamilyMemberResponse> GetFamilyMemberDetailAsync(
        long userId,
        string identifier,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ValidationException("Mã hồ sơ người thân không hợp lệ.");

        var self = await LoadActiveSelfAsync(userId, ct);
        var (parsedId, parsedCode, isSelf) = ParseMemberIdentifier(identifier);

        if (isSelf ||
            (parsedId.HasValue && parsedId.Value == self.Id) ||
            (!string.IsNullOrEmpty(parsedCode) && (string.Equals(parsedCode, self.PatientCode, StringComparison.OrdinalIgnoreCase) || string.Equals(identifier.Trim(), self.PatientCode, StringComparison.OrdinalIgnoreCase))))
        {
            return BuildSelfResponse(self);
        }

        var linksQuery = _db.PatientFamilyLinks
            .AsNoTracking()
            .Include(l => l.MemberPatient)
            .Where(l => l.OwnerPatientId == self.Id && l.RevokedAt == null);

        PatientFamilyLink? link;
        if (parsedId.HasValue)
        {
            link = await linksQuery.FirstOrDefaultAsync(l => l.MemberPatientId == parsedId.Value, ct);
        }
        else
        {
            var trimmed = identifier.Trim();
            link = await linksQuery.FirstOrDefaultAsync(l =>
                l.MemberPatient != null &&
                (l.MemberPatient.PatientCode == parsedCode || l.MemberPatient.PatientCode == trimmed), ct);
        }

        if (link is null || link.MemberPatient is null)
            throw new NotFoundException("FamilyMember", identifier);

        var member = link.MemberPatient;
        if (!string.Equals(member.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            throw new NotFoundException("FamilyMember", identifier);

        var visitsCount = await _db.Visits
            .AsNoTracking()
            .CountAsync(v => v.PatientId == member.Id, ct);

        var activeAppointment = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Slot)
            .ThenInclude(s => s!.Service)
            .Where(a => a.PatientId == member.Id &&
                        a.Status != "CANCELLED" && a.Status != "COMPLETED" && a.Status != "NO_SHOW")
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync(ct);

        var hasActiveAppointment = activeAppointment != null;
        var activeAppointmentDetails = activeAppointment?.Slot?.Service?.Name;

        return BuildMemberResponse(self, member, link, visitsCount, hasActiveAppointment, activeAppointmentDetails);
    }

    public async Task<FamilyMemberResponse> CreateFamilyMemberAsync(
        long userId,
        CreateFamilyMemberRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ValidationException("Dữ liệu tạo hồ sơ người thân không hợp lệ.");

        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length < 2)
            throw new ValidationException("Họ và tên người thân tối thiểu 2 ký tự.");

        if (request.FullName.Trim().Length > 100)
            throw new ValidationException("Họ và tên người thân không được vượt quá 100 ký tự.");

        if (!Regex.IsMatch(request.FullName.Trim(), @"^[\p{L}\s.'-]+$"))
            throw new ValidationException("Họ và tên người thân chỉ được chứa chữ cái và khoảng trắng.");

        if (string.IsNullOrWhiteSpace(request.Relationship))
            throw new ValidationException("Vui lòng chọn mối quan hệ.");

        var trimmedRel = request.Relationship.Trim();
        if (trimmedRel.Equals("Bản thân", StringComparison.OrdinalIgnoreCase) ||
            trimmedRel.Equals("SELF", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Không thể tạo hồ sơ người thân với mối quan hệ là Bản thân.");
        }

        if (string.IsNullOrWhiteSpace(request.Gender))
            throw new ValidationException("Vui lòng chọn giới tính.");

        var canonicalGender = NormalizeGender(request.Gender);
        var dob = ParseBirthDate(request.BirthDate, request.DateOfBirth);

        if (string.IsNullOrWhiteSpace(request.Address) || request.Address.Trim().Length < 5)
            throw new ValidationException("Địa chỉ thường trú tối thiểu 5 ký tự.");

        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            var cleanedPhone = Regex.Replace(request.Phone.Trim(), @"\s+", "");
            if (!Regex.IsMatch(cleanedPhone, @"^0[35789]\d{8}$|^0\d{9}$"))
                throw new ValidationException("Số điện thoại liên hệ không hợp lệ (cần 10 chữ số bắt đầu bằng 0).");
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            if (!Regex.IsMatch(request.Email.Trim(), @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
                throw new ValidationException("Email nhận kết quả không hợp lệ.");
        }

        if (!string.IsNullOrWhiteSpace(request.EmergencyPhone))
        {
            var cleanedEmergencyPhone = Regex.Replace(request.EmergencyPhone.Trim(), @"\s+", "");
            if (!Regex.IsMatch(cleanedEmergencyPhone, @"^0[35789]\d{8}$|^0\d{9}$"))
                throw new ValidationException("Số điện thoại khẩn cấp không hợp lệ.");
        }

        string? legacyHash = null;
        string? hmacHash = null;
        if (!string.IsNullOrWhiteSpace(request.IdentificationNo))
        {
            var normalizedId = IdentificationNumber.Normalize(request.IdentificationNo);
            var key = IdentificationNumber.RequireKey(_hashOptions);
            legacyHash = IdentificationNumber.ComputeSha256(normalizedId);
            hmacHash = IdentificationNumber.ComputeHmac(normalizedId, key);
        }

        if (!string.IsNullOrWhiteSpace(request.BloodType))
        {
            var bt = request.BloodType.Trim().ToUpperInvariant();
            if (bt is not ("A+" or "A-" or "B+" or "B-" or "AB+" or "AB-" or "O+" or "O-"))
                throw new ValidationException("Nhóm máu không hợp lệ (chấp nhận A+, A-, B+, B-, AB+, AB-, O+, O-).");
        }

        var self = await LoadActiveSelfAsync(userId, ct);
        var canonicalRel = ToCanonicalRelationship(trimmedRel, request.Gender);

        // Check if an existing patient matches the identity hash (HMAC first, SHA fallback during transition).
        if (hmacHash != null)
        {
            var existingPatient = await _db.Patients
                .FirstOrDefaultAsync(p => p.IdentificationHashHmac == hmacHash, ct);

            if (existingPatient is null && IdentificationNumber.AllowShaFallback(_hashOptions))
            {
                existingPatient = await _db.Patients
                    .FirstOrDefaultAsync(p => p.IdentificationHash == legacyHash, ct);
            }

            if (existingPatient != null)
            {
                if (existingPatient.Id == self.Id)
                    throw new ValidationException("Không thể thêm chính bản thân vào danh sách hồ sơ người thân.");

                var existingLink = await _db.PatientFamilyLinks
                    .FirstOrDefaultAsync(l => l.OwnerPatientId == self.Id && l.MemberPatientId == existingPatient.Id, ct);

                if (existingLink != null)
                {
                    if (existingLink.RevokedAt == null)
                        throw new ConflictException("Hồ sơ người thân với số định danh này đã tồn tại trong danh sách của bạn.");

                    // Reactivate revoked link; verification must be re-established later.
                    existingLink.RevokedAt = null;
                    existingLink.Relationship = canonicalRel;
                    existingLink.VerificationStatus = "PENDING";
                    existingLink.VerificationMethod = null;
                    existingLink.VerifiedByStaffId = null;
                    existingLink.VerifiedAt = null;
                    existingLink.LegacyAccessUntil = null;
                    await _db.SaveChangesAsync(ct);

                    return BuildMemberResponse(self, existingPatient, existingLink, 0, false, null);
                }

                // Member exists in system but not linked yet -> link it
                var newLink = new PatientFamilyLink
                {
                    OwnerPatientId = self.Id,
                    MemberPatientId = existingPatient.Id,
                    Relationship = canonicalRel,
                    VerificationStatus = "PENDING",
                    VerifiedAt = null,
                    RevokedAt = null
                };

                _db.PatientFamilyLinks.Add(newLink);
                await _db.SaveChangesAsync(ct);

                return BuildMemberResponse(self, existingPatient, newLink, 0, false, null);
            }
        }

        // Create a new Patient entity
        var newPatientCode = await GenerateUniquePatientCodeAsync(ct);
        var newPatient = new Patient
        {
            PatientCode = newPatientCode,
            FullName = request.FullName.Trim(),
            DateOfBirth = dob,
            Gender = canonicalGender,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant(),
            Address = request.Address.Trim(),
            IdentificationHash = legacyHash,
            IdentificationHashHmac = hmacHash,
            HashVersion = hmacHash is null ? null : IdentificationNumber.CurrentHashVersion,
            BloodType = string.IsNullOrWhiteSpace(request.BloodType) ? null : request.BloodType.Trim().ToUpperInvariant(),
            Status = "ACTIVE",
            UserId = null
        };

        _db.Patients.Add(newPatient);
        await _db.SaveChangesAsync(ct);

        var link = new PatientFamilyLink
        {
            OwnerPatientId = self.Id,
            MemberPatientId = newPatient.Id,
            Relationship = canonicalRel,
            VerificationStatus = hmacHash != null ? "PENDING" : "ACTIVE",
            VerifiedAt = null,
            RevokedAt = null
        };

        _db.PatientFamilyLinks.Add(link);
        await _db.SaveChangesAsync(ct);

        return BuildMemberResponse(self, newPatient, link, 0, false, null);
    }

    public async Task<FamilyMemberResponse> UpdateFamilyMemberAsync(
        long userId,
        string identifier,
        UpdateFamilyMemberRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ValidationException("Dữ liệu cập nhật hồ sơ người thân không hợp lệ.");

        if (string.IsNullOrWhiteSpace(identifier))
            throw new ValidationException("Mã hồ sơ người thân không hợp lệ.");

        var self = await LoadActiveSelfAsync(userId, ct);
        var (parsedId, parsedCode, isSelf) = ParseMemberIdentifier(identifier);

        if (isSelf ||
            (parsedId.HasValue && parsedId.Value == self.Id) ||
            (!string.IsNullOrEmpty(parsedCode) && (string.Equals(parsedCode, self.PatientCode, StringComparison.OrdinalIgnoreCase) || string.Equals(identifier.Trim(), self.PatientCode, StringComparison.OrdinalIgnoreCase))))
        {
            throw new ValidationException("Vui lòng sử dụng tính năng cập nhật hồ sơ cá nhân để chỉnh sửa thông tin của chính mình.");
        }

        var linksQuery = _db.PatientFamilyLinks
            .Include(l => l.MemberPatient)
            .Where(l => l.OwnerPatientId == self.Id && l.RevokedAt == null);

        PatientFamilyLink? link;
        if (parsedId.HasValue)
        {
            link = await linksQuery.FirstOrDefaultAsync(l => l.MemberPatientId == parsedId.Value, ct);
        }
        else
        {
            var trimmed = identifier.Trim();
            link = await linksQuery.FirstOrDefaultAsync(l =>
                l.MemberPatient != null &&
                (l.MemberPatient.PatientCode == parsedCode || l.MemberPatient.PatientCode == trimmed), ct);
        }

        if (link is null || link.MemberPatient is null)
            throw new NotFoundException("FamilyMember", identifier);

        var member = link.MemberPatient;
        if (!string.Equals(member.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            throw new NotFoundException("FamilyMember", identifier);

        var linkStatus = link.VerificationStatus?.Trim().ToUpperInvariant();
        var linkMethod = link.VerificationMethod?.Trim().ToUpperInvariant();
        var canEditIdentity = linkStatus == "VERIFIED" && linkMethod is "STAFF" or "OTP";
        var identityFieldsRequested =
            request.FullName is not null ||
            request.Gender is not null ||
            request.Address is not null ||
            request.Phone is not null ||
            request.Email is not null ||
            request.BirthDate is not null ||
            request.DateOfBirth.HasValue;

        if (identityFieldsRequested && (member.UserId is not null || !canEditIdentity))
        {
            throw new ForbiddenException(
                "Không thể sửa thông tin định danh của hồ sơ người thân này. Vui lòng liên hệ quầy tiếp nhận.");
        }
        if (request.FullName is not null)
        {
            var nameTrimmed = request.FullName.Trim();
            if (nameTrimmed.Length < 2)
                throw new ValidationException("Họ và tên người thân tối thiểu 2 ký tự.");
            if (nameTrimmed.Length > 100)
                throw new ValidationException("Họ và tên người thân không được vượt quá 100 ký tự.");
            if (!Regex.IsMatch(nameTrimmed, @"^[\p{L}\s.'-]+$"))
                throw new ValidationException("Họ và tên người thân chỉ được chứa chữ cái và khoảng trắng.");

            member.FullName = nameTrimmed;
        }

        // 2. Validate & update Relationship if provided
        if (request.Relationship is not null)
        {
            var trimmedRel = request.Relationship.Trim();
            if (string.IsNullOrWhiteSpace(trimmedRel))
                throw new ValidationException("Mối quan hệ không được để trống.");

            if (trimmedRel.Equals("Bản thân", StringComparison.OrdinalIgnoreCase) ||
                trimmedRel.Equals("SELF", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("Không thể cập nhật mối quan hệ người thân thành Bản thân.");
            }

            link.Relationship = ToCanonicalRelationship(trimmedRel, member.Gender);
        }

        // 3. Validate & update Gender if provided
        if (request.Gender is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Gender))
                throw new ValidationException("Giới tính không được để trống.");

            member.Gender = NormalizeGender(request.Gender);
        }

        // 4. Validate & update BirthDate if provided
        if (request.BirthDate is not null || request.DateOfBirth.HasValue)
        {
            var dob = ParseBirthDate(request.BirthDate, request.DateOfBirth);
            member.DateOfBirth = dob;
        }

        // 5. Validate & update Address if provided
        if (request.Address is not null)
        {
            var addrTrimmed = request.Address.Trim();
            if (addrTrimmed.Length < 5)
                throw new ValidationException("Địa chỉ thường trú tối thiểu 5 ký tự.");
            if (addrTrimmed.Length > 255)
                throw new ValidationException("Địa chỉ thường trú không được vượt quá 255 ký tự.");

            member.Address = addrTrimmed;
        }

        // 6. Validate & update Phone if provided
        if (request.Phone is not null)
        {
            var trimmedPhone = request.Phone.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedPhone))
            {
                var cleanedPhone = Regex.Replace(trimmedPhone, @"\s+", "");
                if (!Regex.IsMatch(cleanedPhone, @"^0[35789]\d{8}$|^0\d{9}$"))
                    throw new ValidationException("Số điện thoại liên hệ không hợp lệ (cần 10 chữ số bắt đầu bằng 0).");

                member.Phone = cleanedPhone;
            }
            else
            {
                member.Phone = null;
            }
        }

        // 7. Validate & update Email if provided
        if (request.Email is not null)
        {
            var trimmedEmail = request.Email.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedEmail))
            {
                if (!Regex.IsMatch(trimmedEmail, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
                    throw new ValidationException("Email nhận kết quả không hợp lệ.");

                member.Email = trimmedEmail;
            }
            else
            {
                member.Email = null;
            }
        }

        // 8. Validate Emergency Contact fields if provided
        if (request.EmergencyPhone is not null)
        {
            var trimmedEmergencyPhone = request.EmergencyPhone.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedEmergencyPhone))
            {
                var cleanedEmergencyPhone = Regex.Replace(trimmedEmergencyPhone, @"\s+", "");
                if (!Regex.IsMatch(cleanedEmergencyPhone, @"^0[35789]\d{8}$|^0\d{9}$"))
                    throw new ValidationException("Số điện thoại khẩn cấp không hợp lệ.");
            }
        }

        if (request.EmergencyContactName is not null)
        {
            var trimmedName = request.EmergencyContactName.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedName))
            {
                if (trimmedName.Length < 2)
                    throw new ValidationException("Họ tên người liên hệ khẩn cấp tối thiểu 2 ký tự.");
                if (!Regex.IsMatch(trimmedName, @"^[\p{L}\s.'-]+$"))
                    throw new ValidationException("Họ tên người liên hệ khẩn cấp chỉ được chứa chữ cái và khoảng trắng.");
            }
        }

        // 9. Validate & update BloodType if provided
        if (request.BloodType is not null)
        {
            var trimmedBt = request.BloodType.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(trimmedBt))
            {
                if (trimmedBt is not ("A+" or "A-" or "B+" or "B-" or "AB+" or "AB-" or "O+" or "O-"))
                    throw new ValidationException("Nhóm máu không hợp lệ (chấp nhận A+, A-, B+, B-, AB+, AB-, O+, O-).");

                member.BloodType = trimmedBt;
            }
            else
            {
                member.BloodType = null;
            }
        }

        await _db.SaveChangesAsync(ct);

        var visitsCount = await _db.Visits
            .AsNoTracking()
            .CountAsync(v => v.PatientId == member.Id, ct);

        var activeAppointment = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Slot)
            .ThenInclude(s => s!.Service)
            .Where(a => a.PatientId == member.Id &&
                        a.Status != "CANCELLED" && a.Status != "COMPLETED" && a.Status != "NO_SHOW")
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync(ct);

        var hasActiveAppointment = activeAppointment != null;
        var activeAppointmentDetails = activeAppointment?.Slot?.Service?.Name;

        return BuildMemberResponse(self, member, link, visitsCount, hasActiveAppointment, activeAppointmentDetails);
    }

    public async Task RemoveFamilyMemberAsync(
        long userId,
        string identifier,
        string? reason = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ValidationException("Mã hồ sơ người thân không hợp lệ.");

        var self = await LoadActiveSelfAsync(userId, ct);
        var (parsedId, parsedCode, isSelf) = ParseMemberIdentifier(identifier);

        if (isSelf ||
            (parsedId.HasValue && parsedId.Value == self.Id) ||
            (!string.IsNullOrEmpty(parsedCode) && (string.Equals(parsedCode, self.PatientCode, StringComparison.OrdinalIgnoreCase) || string.Equals(identifier.Trim(), self.PatientCode, StringComparison.OrdinalIgnoreCase))))
        {
            throw new ValidationException("Không thể gỡ liên kết hồ sơ của chính bản thân.");
        }

        var linksQuery = _db.PatientFamilyLinks
            .Include(l => l.MemberPatient)
            .Where(l => l.OwnerPatientId == self.Id && l.RevokedAt == null);

        PatientFamilyLink? link;
        if (parsedId.HasValue)
        {
            link = await linksQuery.FirstOrDefaultAsync(l => l.MemberPatientId == parsedId.Value, ct);
        }
        else
        {
            var trimmed = identifier.Trim();
            link = await linksQuery.FirstOrDefaultAsync(l =>
                l.MemberPatient != null &&
                (l.MemberPatient.PatientCode == parsedCode || l.MemberPatient.PatientCode == trimmed), ct);
        }

        if (link is null || link.MemberPatient is null)
            throw new NotFoundException("FamilyMember", identifier);

        var member = link.MemberPatient;
        if (!string.Equals(member.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            throw new NotFoundException("FamilyMember", identifier);

        var hasActiveAppointment = await _db.Appointments
            .AsNoTracking()
            .AnyAsync(a => a.PatientId == member.Id &&
                           a.Status != "CANCELLED" && a.Status != "COMPLETED" && a.Status != "NO_SHOW", ct);

        if (hasActiveAppointment)
        {
            throw new ValidationException("Không thể gỡ liên kết do người thân đang có lịch hẹn khám chưa hoàn tất. Vui lòng hoàn tất hoặc hủy lịch hẹn trước khi gỡ hồ sơ.");
        }

        link.RevokedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        await _db.SaveChangesAsync(ct);
    }

    private async Task<Patient> LoadActiveSelfAsync(long userId, CancellationToken ct)
    {
        if (userId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        var self = await _db.Patients
            .AsNoTracking()
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (self is null)
            throw new NotFoundException("Patient", userId);

        if (self.User is null || self.User.AccountStatus != AccountStatus.ACTIVE)
            throw new UnauthorizedAccessException("Tài khoản bệnh nhân chưa kích hoạt hoặc đã bị khóa.");

        if (!string.Equals(self.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Hồ sơ bệnh nhân đang ngừng hoạt động.");

        return self;
    }

    internal static (long? Id, string? Code, bool IsSelf) ParseMemberIdentifier(string identifier)
    {
        var trimmed = identifier.Trim();

        if (trimmed.Equals("me", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null, true);
        }

        if (long.TryParse(trimmed, out var numericId) && numericId > 0)
        {
            return (numericId, null, false);
        }

        if (trimmed.StartsWith("FAM-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("FAM", StringComparison.OrdinalIgnoreCase))
        {
            var numPart = trimmed.StartsWith("FAM-", StringComparison.OrdinalIgnoreCase)
                ? trimmed[4..]
                : trimmed[3..];

            if (long.TryParse(numPart, out var famId) && famId > 0)
            {
                return (famId, null, false);
            }
        }

        var code = trimmed.TrimStart('#');
        return (null, code, false);
    }

    private static FamilyMemberResponse BuildSelfResponse(Patient self)
    {
        var isSelfVerified = !string.IsNullOrWhiteSpace(self.IdentificationHash);
        return new FamilyMemberResponse(
            Id: $"FAM-{self.Id:D3}",
            MemberPatientId: self.Id,
            FullName: self.FullName,
            Relationship: "Bản thân",
            RelationshipBadgeColor: "slate",
            PatientCode: self.PatientCode,
            SubStatus: "Ủy quyền toàn phần",
            IsPrimaryAccount: true,
            AvatarUrl: null,
            BirthDate: FormatDate(self.DateOfBirth),
            Age: CalculateAge(self.DateOfBirth),
            Gender: MapGender(self.Gender),
            Nationality: "Việt Nam",
            IdentificationNo: isSelfVerified ? MaskedVerifiedText : UnverifiedIdentityText,
            IdentityIssueDate: null,
            IdentityIssuePlace: null,
            IsIdentityVerified: isSelfVerified,
            Phone: self.Phone,
            Email: self.Email ?? self.User?.Email,
            Address: self.Address,
            Guardian: null,
            EmergencyContactName: null,
            EmergencyContactRelation: null,
            EmergencyPhone: null,
            LatestVisit: null,
            FollowUpSchedule: null,
            ServicePackage: null,
            ClinicalNotes: null,
            HasActiveAppointment: false,
            ActiveAppointmentDetails: null,
            OutpatientVisitsCount: 0,
            ActivePrescriptionsCount: 0,
            Vitals: new FamilyMemberVitalsDto(self.BloodType, null, null),
            Permissions: new FamilyMemberPermissionsDto(
                CanManage: true,
                CanBookAppointments: true,
                CanViewMedicalRecords: true,
                RelationshipStatus: "ACTIVE",
                ConsentVerifiedAt: null
            )
        );
    }

    private static FamilyMemberResponse BuildMemberResponse(
        Patient self,
        Patient member,
        PatientFamilyLink link,
        int visitsCount,
        bool hasActiveAppointment,
        string? activeAppointmentDetails)
    {
        var relationship = MapRelationship(link.Relationship, member.Gender);
        var badgeColor = GetBadgeColor(relationship);
        var status = !string.IsNullOrWhiteSpace(link.VerificationStatus)
            ? link.VerificationStatus.ToUpperInvariant()
            : "ACTIVE";
        var method = link.VerificationMethod?.Trim().ToUpperInvariant();
        var canViewPhi = status == "VERIFIED" && method is "STAFF" or "OTP";
        var isVerified = canViewPhi;

        var canManage = status is "VERIFIED" or "ACTIVE";
        var subStatus = canManage ? GetSubStatus(relationship) : "Chỉ đọc";

        var isChild = relationship.StartsWith("Con", StringComparison.OrdinalIgnoreCase);
        var isParent = relationship.Contains("Bố") || relationship.Contains("Mẹ");

        var clinicalNotes = isChild
            ? "Khám Nhi tự nguyện: Cần có phụ huynh đi cùng khi thăm khám"
            : isParent
                ? "Ủy quyền nhận kết quả xét nghiệm và quản lý lịch khám định kỳ"
                : null;

        return new FamilyMemberResponse(
            Id: $"FAM-{member.Id:D3}",
            MemberPatientId: member.Id,
            FullName: member.FullName,
            Relationship: relationship,
            RelationshipBadgeColor: badgeColor,
            PatientCode: member.PatientCode,
            SubStatus: subStatus,
            IsPrimaryAccount: false,
            AvatarUrl: null,
            BirthDate: FormatDate(member.DateOfBirth),
            Age: CalculateAge(member.DateOfBirth),
            Gender: MapGender(member.Gender),
            Nationality: "Việt Nam",
            IdentificationNo: isVerified ? MaskedVerifiedText : UnverifiedIdentityText,
            IdentityIssueDate: null,
            IdentityIssuePlace: null,
            IsIdentityVerified: isVerified,
            Phone: member.Phone,
            Email: member.Email,
            Address: member.Address ?? self.Address,
            Guardian: isChild ? self.FullName : null,
            EmergencyContactName: self.FullName,
            EmergencyContactRelation: isChild ? "Bố ruột (Giám hộ)" : "Chủ hộ / Người giám hộ",
            EmergencyPhone: self.Phone,
            LatestVisit: null,
            FollowUpSchedule: null,
            ServicePackage: null,
            ClinicalNotes: clinicalNotes,
            HasActiveAppointment: hasActiveAppointment,
            ActiveAppointmentDetails: activeAppointmentDetails,
            OutpatientVisitsCount: visitsCount,
            ActivePrescriptionsCount: 0,
            Vitals: new FamilyMemberVitalsDto(member.BloodType, null, null),
            Permissions: new FamilyMemberPermissionsDto(
                CanManage: canManage,
                CanBookAppointments: true,
                CanViewMedicalRecords: canViewPhi,
                RelationshipStatus: status,
                ConsentVerifiedAt: canViewPhi ? link.VerifiedAt?.ToString("dd/MM/yyyy") : null
            )
        );
    }

    private static string? FormatDate(DateOnly? date)
        => date?.ToString("dd/MM/yyyy");

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

    private static string? MapGender(string? gender)
    {
        if (string.IsNullOrWhiteSpace(gender))
            return null;

        var normalized = gender.Trim().ToUpperInvariant();
        return normalized switch
        {
            "MALE" or "NAM" => "Nam",
            "FEMALE" or "NỮ" or "NU" => "Nữ",
            "OTHER" or "KHÁC" or "KHAC" => "Khác",
            _ => gender
        };
    }

    private static string MapRelationship(string? relationship, string? gender)
    {
        if (string.IsNullOrWhiteSpace(relationship))
            return "Khác";

        var trimmed = relationship.Trim();
        var normalizedGender = gender?.Trim().ToUpperInvariant();
        var isFemale = normalizedGender is "FEMALE" or "NỮ" or "NU";

        return trimmed.ToUpperInvariant() switch
        {
            "SELF" => "Bản thân",
            "CHILD" => isFemale ? "Con gái" : "Con trai",
            "SON" => "Con trai",
            "DAUGHTER" => "Con gái",
            "SPOUSE" => isFemale ? "Vợ" : "Chồng",
            "WIFE" => "Vợ",
            "HUSBAND" => "Chồng",
            "PARENT" => isFemale ? "Mẹ ruột" : "Bố ruột",
            "FATHER" => "Bố ruột",
            "MOTHER" => "Mẹ ruột",
            "OTHER" => "Khác",
            _ => trimmed
        };
    }

    private static string GetBadgeColor(string relationship)
    {
        return relationship switch
        {
            "Bản thân" => "slate",
            "Con trai" or "Con gái" => "blue",
            "Vợ" or "Chồng" => "rose",
            "Mẹ ruột" or "Bố ruột" => "amber",
            _ => "slate"
        };
    }

    private static string GetSubStatus(string relationship)
    {
        return relationship switch
        {
            "Con trai" or "Con gái" => "Hồ sơ nhi khoa ngoại trú",
            "Vợ" or "Chồng" => "Hồ sơ liên kết gia đình",
            "Mẹ ruột" or "Bố ruột" => "Hồ sơ người cao tuổi",
            _ => "Hồ sơ thành viên liên kết"
        };
    }

    private static string NormalizeGender(string input)
    {
        var trimmed = input.Trim().ToUpperInvariant();
        return trimmed switch
        {
            "NAM" or "MALE" => "MALE",
            "NỮ" or "NU" or "FEMALE" => "FEMALE",
            "KHÁC" or "KHAC" or "OTHER" => "OTHER",
            _ => throw new ValidationException("Giới tính không hợp lệ (chấp nhận: Nam, Nữ, Khác).")
        };
    }

    private static string ToCanonicalRelationship(string input, string? gender)
    {
        var trimmed = input.Trim().ToUpperInvariant();
        var isFemale = gender?.Trim().ToUpperInvariant() is "FEMALE" or "NỮ" or "NU";

        return trimmed switch
        {
            "CON TRAI" or "CON GÁI" or "CON" or "CHILD" or "SON" or "DAUGHTER" => "CHILD",
            "VỢ" or "CHỒNG" or "SPOUSE" or "WIFE" or "HUSBAND" => "SPOUSE",
            "BỐ RUỘT" or "MẸ RUỘT" or "BỐ" or "MẸ" or "CHA" or "PARENT" or "FATHER" or "MOTHER" => "PARENT",
            "OTHER" or "KHÁC" or "KHAC" => "OTHER",
            _ => trimmed
        };
    }

    private static DateOnly ParseBirthDate(string? birthDateStr, DateOnly? dateOfBirth)
    {
        if (dateOfBirth.HasValue)
        {
            ValidateBirthDate(dateOfBirth.Value);
            return dateOfBirth.Value;
        }

        if (string.IsNullOrWhiteSpace(birthDateStr))
            throw new ValidationException("Vui lòng nhập ngày sinh.");

        var trimmed = birthDateStr.Trim();

        // Kiểm tra chặt chẽ năm sinh phải có đúng 4 chữ số: DD/MM/YYYY hoặc YYYY-MM-DD
        var matchDmy = Regex.Match(trimmed, @"^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})$");
        var matchYmd = Regex.Match(trimmed, @"^(\d{4})[/.-](\d{1,2})[/.-](\d{1,2})$");

        if (!matchDmy.Success && !matchYmd.Success)
            throw new ValidationException("Định dạng ngày sinh phải là DD/MM/YYYY hoặc YYYY-MM-DD, năm sinh gồm 4 chữ số (ví dụ: 15/08/2018).");

        string[] formats = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "yyyy/MM/dd", "dd-MM-yyyy"];

        if (DateOnly.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedExact))
        {
            ValidateBirthDate(parsedExact);
            return parsedExact;
        }

        throw new ValidationException("Ngày sinh không hợp lệ (vui lòng kiểm tra lại ngày và tháng sinh trên lịch).");
    }

    private static void ValidateBirthDate(DateOnly dob)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (dob > today)
            throw new ValidationException("Ngày sinh không được lớn hơn ngày hiện tại.");

        if (dob.Year < 1900)
            throw new ValidationException("Năm sinh không hợp lệ (năm sinh từ năm 1900 đến nay).");
    }

    private async Task<string> GenerateUniquePatientCodeAsync(CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
        {
            var candidate = $"PAT-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
            if (!await _db.Patients.AnyAsync(p => p.PatientCode == candidate, ct))
                return candidate;
        }

        return $"PAT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
    }
}
