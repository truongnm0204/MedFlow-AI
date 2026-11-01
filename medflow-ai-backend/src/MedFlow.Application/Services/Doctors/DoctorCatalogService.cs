using System.Globalization;
using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Doctors;

public sealed class DoctorCatalogService : IDoctorCatalogService
{
    private const decimal DefaultExaminationPrice = 200_000m;

    private static readonly IReadOnlyDictionary<string, string> LegacySlugToDoctorCode =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["doc-01"] = "DOC-001",
            ["doc-02"] = "DOC-002",
        };

    private readonly IApplicationDbContext _db;

    public DoctorCatalogService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<DoctorDetailDto> GetByIdOrCodeAsync(
        string idOrCode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
            throw new ValidationException("Mã bác sĩ không được để trống.");

        var normalized = idOrCode.Trim();
        if (normalized.Length > 64)
            throw new ValidationException("Mã bác sĩ không được vượt quá 64 ký tự.");

        if (!normalized.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_'))
            throw new ValidationException("Mã bác sĩ chứa ký tự không hợp lệ.");

        var resolvedCode = LegacySlugToDoctorCode.TryGetValue(normalized, out var mappedCode)
            ? mappedCode
            : normalized;

        var isNumericId = long.TryParse(resolvedCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId);
        var upperCode = resolvedCode.ToUpperInvariant();

        var doctor = await _db.Doctors
            .AsNoTracking()
            .Include(d => d.Staff)
                .ThenInclude(s => s!.Department)
                    .ThenInclude(dept => dept!.HospitalRooms)
            .Include(d => d.Specialty)
            .Include(d => d.ServiceAuthorizations)
                .ThenInclude(a => a.Service)
                    .ThenInclude(svc => svc!.ServicePrices)
            .Include(d => d.ExaminationSlots)
                .ThenInclude(slot => slot.Room)
            .Where(d =>
                d.IsActive == true &&
                d.Staff != null &&
                d.Staff.Status == "ACTIVE")
            .FirstOrDefaultAsync(
                d =>
                    (isNumericId && d.Id == numericId) ||
                    d.DoctorCode.ToUpper() == upperCode ||
                    (d.Staff != null && d.Staff.StaffCode.ToUpper() == upperCode),
                ct);

        if (doctor is null)
            throw new NotFoundException("Doctor", normalized);

        return MapToDetailDto(doctor, DateTime.UtcNow);
    }

    private static DoctorDetailDto MapToDetailDto(Doctor doctor, DateTime now)
    {
        var specialtyCode = doctor.Specialty?.SpecialtyCode?.Trim() ?? string.Empty;
        var specialtyName = !string.IsNullOrWhiteSpace(doctor.Specialty?.Name)
            ? doctor.Specialty!.Name.Trim()
            : "Nội tổng quát";

        var departmentName = !string.IsNullOrWhiteSpace(doctor.Staff?.Department?.Name)
            ? doctor.Staff!.Department!.Name.Trim()
            : $"Khoa {specialtyName}";

        var rawTitle = doctor.ProfessionalTitle?.Trim() ?? "BS";
        var normalizedTitle = NormalizeAcademicTitle(rawTitle);
        var displayName = FormatDisplayName(doctor.Staff?.FullName, rawTitle);

        var qualificationsSummary = !string.IsNullOrWhiteSpace(doctor.QualificationsSummary)
            ? doctor.QualificationsSummary!.Trim()
            : "Bác sĩ chuyên khoa";

        var roleTitle = $"{qualificationsSummary} - {departmentName}";
        var room = ResolveDoctorRoom(doctor);
        var price = ResolveConsultationPrice(doctor, now);
        var yearsOfExperience = Math.Max(0, (int)(doctor.YearsOfExperience ?? 0));
        var avatarUrl = !string.IsNullOrWhiteSpace(doctor.ProfileImageUrl)
            ? doctor.ProfileImageUrl!.Trim()
            : ResolveDefaultAvatarUrl(doctor.Id, specialtyCode);

        var hasOpenSlotOrOnlineService =
            (doctor.ExaminationSlots?.Any(s => string.Equals(s.Status, "OPEN", StringComparison.OrdinalIgnoreCase)) ?? false) ||
            (doctor.ServiceAuthorizations?.Any(a =>
                a.IsActive == true &&
                a.Service != null &&
                a.Service.IsActive == true &&
                string.Equals(a.Service.ServiceType, "EXAMINATION", StringComparison.OrdinalIgnoreCase)) ?? false);

        var statusText = hasOpenSlotOrOnlineService
            ? "Đang tiếp nhận lịch khám ngoại trú"
            : "Lịch khám theo phân công chuyên khoa";

        var education = BuildEducationItems(
            doctor.Id,
            qualificationsSummary,
            specialtyName,
            yearsOfExperience,
            doctor.LicenseNumber);

        var biography = BuildBiography(
            doctor.ProfessionalBio,
            yearsOfExperience,
            specialtyCode,
            specialtyName,
            departmentName);

        var hashtags = BuildHashtags(specialtyCode, specialtyName);
        var weeklySchedule = BuildWeeklySchedule(doctor, specialtyCode);

        return new DoctorDetailDto(
            Id: doctor.Id.ToString(CultureInfo.InvariantCulture),
            DoctorCode: doctor.DoctorCode,
            StaffCode: doctor.Staff?.StaffCode ?? string.Empty,
            Name: displayName,
            Title: normalizedTitle,
            RoleTitle: roleTitle,
            Specialty: departmentName,
            SpecialtyCode: specialtyCode,
            DepartmentName: departmentName,
            Room: room,
            StatusText: statusText,
            Price: price,
            AvatarUrl: avatarUrl,
            YearsOfExperience: yearsOfExperience,
            LicenseNumber: doctor.LicenseNumber,
            Education: education,
            Biography: biography,
            Hashtags: hashtags,
            WeeklySchedule: weeklySchedule);
    }

    private static string NormalizeAcademicTitle(string rawTitle)
    {
        var upper = rawTitle.Replace(" ", string.Empty).ToUpperInvariant();
        return upper switch
        {
            "BS.CKI" or "BSCKI" => "BSCKI",
            "BS.CKII" or "BSCKII" => "BSCKII",
            "THS.BS" or "THS" => "ThS",
            "TS.BS" or "TS" => "TS",
            "PGS.TS.BS" or "PGS.TS" or "PGS" => "PGS",
            "GS.TS.BS" or "GS.TS" or "GS" => "GS",
            _ => rawTitle,
        };
    }

    private static string FormatDisplayName(string? fullName, string professionalTitle)
    {
        var cleanName = string.IsNullOrWhiteSpace(fullName) ? "Bác sĩ Chuyên khoa" : fullName.Trim();
        if (string.IsNullOrWhiteSpace(professionalTitle))
            return cleanName;

        if (cleanName.StartsWith(professionalTitle, StringComparison.OrdinalIgnoreCase))
            return cleanName;

        if (cleanName.StartsWith("BS ", StringComparison.OrdinalIgnoreCase))
            return $"{professionalTitle} {cleanName[3..].TrimStart()}";

        return $"{professionalTitle} {cleanName}";
    }

    private static string ResolveDoctorRoom(Doctor doctor)
    {
        var slotRoom = doctor.ExaminationSlots?
            .Where(s =>
                s.Room is not null &&
                string.Equals(s.Room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => string.Equals(s.Status, "OPEN", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(s => s.StartsAt)
            .ThenBy(s => s.Id)
            .Select(s => s.Room!)
            .FirstOrDefault();

        if (slotRoom is not null)
            return FormatHospitalRoom(slotRoom);

        var deptRoom = doctor.Staff?.Department?.HospitalRooms?
            .Where(r =>
                string.Equals(r.RoomType, "EXAMINATION", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Id)
            .FirstOrDefault();

        if (deptRoom is not null)
            return FormatHospitalRoom(deptRoom);

        return "Phòng 101 - Tầng 1, Khu Demo";
    }

    private static string FormatHospitalRoom(HospitalRoom room)
    {
        var roomLabel = ExtractRoomLabel(room);
        var location = !string.IsNullOrWhiteSpace(room.Location)
            ? room.Location!.Trim()
            : "Tầng 1, Khu Demo";

        return !string.IsNullOrWhiteSpace(roomLabel)
            ? $"Phòng {roomLabel} - {location}"
            : $"{room.Name} - {location}";
    }

    private static string ExtractRoomLabel(HospitalRoom room)
    {
        if (!string.IsNullOrWhiteSpace(room.Name))
        {
            var parts = room.Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var lastToken = parts[^1];
            if (lastToken.All(char.IsDigit))
                return lastToken;
        }

        return room.RoomCode?.Trim() ?? string.Empty;
    }

    private static decimal ResolveConsultationPrice(Doctor doctor, DateTime now)
    {
        var authorizedExamServices = doctor.ServiceAuthorizations?
            .Where(a =>
                a.IsActive == true &&
                a.Service is not null &&
                a.Service.IsActive == true &&
                string.Equals(a.Service.ServiceType, "EXAMINATION", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Service!)
            .OrderBy(s => s.ServiceCode.EndsWith("-01", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(s => s.Id)
            .ToList();

        if (authorizedExamServices is null || authorizedExamServices.Count == 0)
            return DefaultExaminationPrice;

        foreach (var service in authorizedExamServices)
        {
            var activePrice = service.ServicePrices?
                .Where(p =>
                    p.PriceAmount.HasValue &&
                    (!p.EffectiveFrom.HasValue || p.EffectiveFrom.Value <= now) &&
                    (!p.EffectiveTo.HasValue || p.EffectiveTo.Value >= now))
                .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
                .ThenByDescending(p => p.Id)
                .FirstOrDefault();

            if (activePrice?.PriceAmount is > 0)
                return activePrice.PriceAmount.Value;
        }

        return DefaultExaminationPrice;
    }

    private static string ResolveDefaultAvatarUrl(long doctorId, string specialtyCode)
    {
        if (string.Equals(specialtyCode, "CARD", StringComparison.OrdinalIgnoreCase) || doctorId % 2 == 0)
            return "/assets/admin-doctors/list-02.png";

        return "/assets/admin-doctors/list-01.png";
    }

    private static IReadOnlyList<DoctorEducationItemDto> BuildEducationItems(
        long doctorId,
        string qualificationsSummary,
        string specialtyName,
        int yearsOfExperience,
        string? licenseNumber)
    {
        var experienceBadge = yearsOfExperience > 0
            ? $"{yearsOfExperience} năm kinh nghiệm"
            : null;

        var licenseBadge = !string.IsNullOrWhiteSpace(licenseNumber)
            ? $"CCHN: {licenseNumber.Trim()}"
            : null;

        return
        [
            new DoctorEducationItemDto(
                Id: $"edu-{doctorId}-1",
                Title: "Bác sĩ Đa khoa",
                Institution: "Đại học Y Hà Nội (Hanoi Medical University)",
                Badge: "Tốt nghiệp Chính quy",
                Type: "degree"),
            new DoctorEducationItemDto(
                Id: $"edu-{doctorId}-2",
                Title: $"{qualificationsSummary} - Chuyên ngành {specialtyName}",
                Institution: "Đào tạo Sau đại học Chuyên khoa Lâm sàng",
                Badge: experienceBadge,
                Type: "specialist"),
            new DoctorEducationItemDto(
                Id: $"edu-{doctorId}-3",
                Title: $"Chứng chỉ hành nghề khám bệnh, chữa bệnh chuyên khoa {specialtyName}",
                Institution: "Bộ Y tế cấp phép hành nghề",
                Badge: licenseBadge,
                Type: "certificate"),
        ];
    }

    private static string BuildBiography(
        string? professionalBio,
        int yearsOfExperience,
        string specialtyCode,
        string specialtyName,
        string departmentName)
    {
        var baseBio = !string.IsNullOrWhiteSpace(professionalBio)
            ? professionalBio.Trim()
            : $"Bác sĩ chuyên khoa công tác tại {departmentName}.";

        var experiencePrefix = yearsOfExperience > 0
            ? $"Hơn {yearsOfExperience} năm kinh nghiệm lâm sàng chuyên sâu trong lĩnh vực {specialtyName}. "
            : string.Empty;

        if (string.Equals(specialtyCode, "GI", StringComparison.OrdinalIgnoreCase))
        {
            return $"{experiencePrefix}{baseBio} Chuyên thăm khám, tư vấn và điều trị các bệnh lý đường tiêu hóa: trào ngược dạ dày thực quản (GERD), viêm loét dạ dày - tá tràng, nhiễm H. pylori và bệnh lý gan mật.";
        }

        if (string.Equals(specialtyCode, "CARD", StringComparison.OrdinalIgnoreCase))
        {
            return $"{experiencePrefix}{baseBio} Chuyên thăm khám, tầm soát và điều trị các bệnh lý tim mạch: tăng huyết áp, thiếu máu cơ tim, rối loạn nhịp tim và tư vấn dự phòng tim mạch.";
        }

        return $"{experiencePrefix}{baseBio}";
    }

    private static IReadOnlyList<string> BuildHashtags(string specialtyCode, string specialtyName)
    {
        if (string.Equals(specialtyCode, "GI", StringComparison.OrdinalIgnoreCase))
        {
            return ["#NoiSoiTieuHoa", "#Gerd", "#DaDayTaTrang", "#GanMat", "#KhamNgoaiTru"];
        }

        if (string.Equals(specialtyCode, "CARD", StringComparison.OrdinalIgnoreCase))
        {
            return ["#TimMach", "#TangHuyetAp", "#DienTamDoECG", "#TamSoatTimMach", "#KhamNgoaiTru"];
        }

        var normalizedTag = "#" + specialtyName.Replace(" ", string.Empty);
        return [normalizedTag, "#KhamNgoaiTru", "#BacSiChuyenKhoa", "#MedFlowAI"];
    }

    private static IReadOnlyList<DoctorWeeklyScheduleDayDto> BuildWeeklySchedule(
        Doctor doctor,
        string specialtyCode)
    {
        var openSlots = doctor.ExaminationSlots?
            .Where(s =>
                string.Equals(s.Status, "OPEN", StringComparison.OrdinalIgnoreCase) &&
                s.StartsAt.HasValue &&
                s.EndsAt.HasValue)
            .OrderBy(s => s.StartsAt)
            .ToList();

        var firstOpenSlot = openSlots?.FirstOrDefault();
        var openSlotWindow = firstOpenSlot is not null
            ? $"{firstOpenSlot.StartsAt!.Value:HH:mm} - {firstOpenSlot.EndsAt!.Value:HH:mm}"
            : null;

        if (string.Equals(specialtyCode, "CARD", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new DoctorWeeklyScheduleDayDto(
                    DayOfWeek: "Thứ Ba",
                    HasAvailableSlot: true,
                    MorningTime: "08:00 - 11:30",
                    AfternoonTime: "13:30 - 16:30",
                    Note: null,
                    StatusTag: "Đang nhận bệnh",
                    IsActive: false),
                new DoctorWeeklyScheduleDayDto(
                    DayOfWeek: "Thứ Tư",
                    HasAvailableSlot: true,
                    MorningTime: openSlotWindow ?? "08:30 - 11:30",
                    AfternoonTime: "14:00 - 16:30",
                    Note: null,
                    StatusTag: "Đang nhận bệnh",
                    IsActive: true),
                new DoctorWeeklyScheduleDayDto(
                    DayOfWeek: "Thứ Sáu",
                    HasAvailableSlot: true,
                    MorningTime: "08:00 - 11:30",
                    AfternoonTime: null,
                    Note: "Buổi chiều: Hội chẩn tim mạch",
                    StatusTag: "Đang nhận bệnh",
                    IsActive: false),
            ];
        }

        return
        [
            new DoctorWeeklyScheduleDayDto(
                DayOfWeek: "Thứ Hai",
                HasAvailableSlot: true,
                MorningTime: "08:00 - 11:30",
                AfternoonTime: "14:00 - 16:30",
                Note: null,
                StatusTag: "Đang nhận bệnh",
                IsActive: false),
            new DoctorWeeklyScheduleDayDto(
                DayOfWeek: "Thứ Ba",
                HasAvailableSlot: true,
                MorningTime: openSlotWindow ?? "08:00 - 11:30",
                AfternoonTime: "14:00 - 16:30",
                Note: null,
                StatusTag: "Đang nhận bệnh",
                IsActive: true),
            new DoctorWeeklyScheduleDayDto(
                DayOfWeek: "Thứ Năm",
                HasAvailableSlot: true,
                MorningTime: "08:00 - 11:30",
                AfternoonTime: null,
                Note: "Buổi chiều: Nội soi tiêu hóa",
                StatusTag: "Đang nhận bệnh",
                IsActive: false),
        ];
    }
}
