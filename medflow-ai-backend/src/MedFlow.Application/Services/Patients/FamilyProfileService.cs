using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Patients;

public sealed class FamilyProfileService : IFamilyProfileService
{
    private const string MaskedVerifiedText = "****** (Đã xác thực CCCD)";
    private const string UnverifiedIdentityText = "Chưa cập nhật CCCD";

    private readonly IApplicationDbContext _db;

    public FamilyProfileService(IApplicationDbContext db)
    {
        _db = db;
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
        var isVerified = !string.IsNullOrWhiteSpace(member.IdentificationHash);
        var status = !string.IsNullOrWhiteSpace(link.VerificationStatus)
            ? link.VerificationStatus.ToUpperInvariant()
            : "ACTIVE";

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
                CanViewMedicalRecords: true,
                RelationshipStatus: status,
                ConsentVerifiedAt: link.VerifiedAt?.ToString("dd/MM/yyyy")
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
}
