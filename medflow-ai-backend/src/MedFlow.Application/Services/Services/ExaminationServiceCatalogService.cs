using System.Globalization;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Services;

public class ExaminationServiceCatalogService : IExaminationServiceCatalogService
{
    private static readonly HashSet<string> ValidCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "all",
        "specialty",
        "package",
        "pediatric",
        "general",
    };

    private static readonly HashSet<string> ValidPriceDurationRanges = new(StringComparer.OrdinalIgnoreCase)
    {
        "all",
        "under-200k",
        "200k-250k",
        "over-250k",
        "duration-under-20",
        "duration-over-20",
    };

    private static readonly Dictionary<string, string> DepartmentCodeToFrontendId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GI"] = "dept-th",
        ["CARD"] = "dept-tm",
        ["OPD"] = "dept-noi",
        ["ENT"] = "dept-tmh",
        ["PED"] = "dept-nhi",
        ["MSK"] = "dept-cxk",
        ["NEU"] = "dept-tk",
        ["ENDO"] = "dept-nt",
        ["CHK"] = "dept-ts",
    };

    private static readonly Dictionary<string, string> LegacySlugToServiceCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["srv-th-01"] = "EX-GI-01",
        ["srv-tm-01"] = "EX-CARD-01",
        ["srv-th-02"] = "EX-GI-02",
        ["srv-tm-02"] = "EX-CARD-02",
    };

    private static readonly IReadOnlyList<ServiceStandardStepDto> DefaultStandardSteps =
    [
        new(1, "Tiếp đón & Đo sinh hiệu", "Đo huyết áp, mạch, thân nhiệt, chỉ số BMI (chiều cao, cân nặng) tại quầy tiếp đón điều dưỡng."),
        new(2, "Khám lâm sàng chuyên khoa", "Bác sĩ chuyên khoa hỏi bệnh sử kỹ lưỡng, thăm khám lâm sàng và đánh giá triệu chứng thực thể."),
        new(3, "Chỉ định cận lâm sàng", "Nếu cần thiết: chỉ định xét nghiệm máu, siêu âm hoặc chẩn đoán hình ảnh phù hợp theo nhu cầu bệnh học."),
        new(4, "Kết luận & Đơn thuốc số", "Đọc kết quả, chẩn đoán xác định, kê đơn thuốc điện tử và tư vấn chế độ sinh hoạt, hẹn tái khám."),
    ];

    private readonly IApplicationDbContext _db;

    public ExaminationServiceCatalogService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ServiceSearchResponseDto> SearchAsync(
        SearchExaminationServicesRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trimmedQuery = request.Query?.Trim();
        if (!string.IsNullOrEmpty(trimmedQuery) && trimmedQuery.Length > 100)
            throw new ValidationException("Từ khóa tìm kiếm không được vượt quá 100 ký tự.");

        if (request.Page < 1)
            throw new ValidationException("Số trang phải lớn hơn hoặc bằng 1.");

        if (request.PageSize < 1 || request.PageSize > 50)
            throw new ValidationException("Kích thước trang phải từ 1 đến 50.");

        var category = string.IsNullOrWhiteSpace(request.Category)
            ? "all"
            : request.Category.Trim().ToLowerInvariant();
        if (!ValidCategories.Contains(category))
            throw new ValidationException("Danh mục dịch vụ không hợp lệ.");

        var priceDurationRange = string.IsNullOrWhiteSpace(request.PriceDurationRange)
            ? "all"
            : request.PriceDurationRange.Trim().ToLowerInvariant();
        if (!ValidPriceDurationRanges.Contains(priceDurationRange))
            throw new ValidationException("Bộ lọc mức giá hoặc thời lượng không hợp lệ.");

        var departmentFilter = string.IsNullOrWhiteSpace(request.Department)
            ? "all"
            : request.Department.Trim();

        var services = await _db.Services
            .AsNoTracking()
            .Include(s => s.Department)
            .Include(s => s.Specialty)
            .Include(s => s.ServicePrices)
            .Where(s => s.ServiceType == "EXAMINATION" && s.IsActive == true)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var mapped = services
            .Select(s => (Entity: s, Dto: MapToDto(s, now)))
            .ToList();

        IEnumerable<(Service Entity, ExaminationServiceDto Dto)> filtered = mapped;

        // 1. Keyword filter (query)
        if (!string.IsNullOrEmpty(trimmedQuery))
        {
            filtered = filtered.Where(item =>
                item.Dto.Name.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
                item.Dto.ServiceCode.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
                item.Dto.DepartmentName.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
                item.Dto.Description.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
                item.Dto.Tags.Any(tag => tag.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase)));
        }

        // 2. Category filter
        if (!string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(item =>
                string.Equals(item.Dto.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        // 3. Department filter
        if (!string.Equals(departmentFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(item => MatchesDepartment(item.Entity, item.Dto, departmentFilter));
        }

        // 4. Price / duration range filter
        if (!string.Equals(priceDurationRange, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = priceDurationRange switch
            {
                "under-200k" => filtered.Where(item => item.Dto.Price <= 200_000m),
                "200k-250k" => filtered.Where(item => item.Dto.Price > 200_000m && item.Dto.Price <= 250_000m),
                "over-250k" => filtered.Where(item => item.Dto.Price > 250_000m),
                "duration-under-20" => filtered.Where(item => item.Dto.DurationMinutes <= 20),
                "duration-over-20" => filtered.Where(item => item.Dto.DurationMinutes > 20),
                _ => filtered,
            };
        }

        var filteredList = filtered.Select(item => item.Dto).ToList();
        var total = filteredList.Count;
        var totalPages = total == 0 ? 1 : (int)Math.Ceiling((double)total / request.PageSize);
        var pagedData = filteredList
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new ServiceSearchResponseDto(
            Data: pagedData,
            Total: total,
            Page: request.Page,
            PageSize: request.PageSize,
            TotalPages: totalPages);
    }

    public async Task<ExaminationServiceDto> GetByIdOrCodeAsync(
        string idOrCode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
            throw new ValidationException("Mã dịch vụ khám không được để trống.");

        var normalized = idOrCode.Trim();
        var resolvedCode = LegacySlugToServiceCode.TryGetValue(normalized, out var mappedCode)
            ? mappedCode
            : normalized;

        var isNumericId = long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId);
        var upperCode = resolvedCode.ToUpperInvariant();

        var service = await _db.Services
            .AsNoTracking()
            .Include(s => s.Department)
            .Include(s => s.Specialty)
            .Include(s => s.ServicePrices)
            .Where(s => s.ServiceType == "EXAMINATION" && s.IsActive == true)
            .FirstOrDefaultAsync(
                s => (isNumericId && s.Id == numericId) || s.ServiceCode.ToUpper() == upperCode,
                ct);

        if (service is null)
            throw new NotFoundException("ExaminationService", normalized);

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        return MapToDto(service, now);
    }

    private static bool MatchesDepartment(Service entity, ExaminationServiceDto dto, string departmentFilter)
    {
        if (string.Equals(dto.DepartmentId, departmentFilter, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(entity.Department?.DepartmentCode) &&
            string.Equals(entity.Department.DepartmentCode, departmentFilter, StringComparison.OrdinalIgnoreCase))
            return true;

        if (entity.DepartmentId.HasValue &&
            string.Equals(entity.DepartmentId.Value.ToString(CultureInfo.InvariantCulture), departmentFilter, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(dto.DepartmentName, departmentFilter, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static ExaminationServiceDto MapToDto(Service service, DateTime now)
    {
        var deptCode = service.Department?.DepartmentCode?.Trim() ?? string.Empty;
        var specialtyCode = service.Specialty?.SpecialtyCode?.Trim() ?? string.Empty;

        var departmentId = !string.IsNullOrEmpty(deptCode) && DepartmentCodeToFrontendId.TryGetValue(deptCode, out var mappedDeptId)
            ? mappedDeptId
            : (!string.IsNullOrEmpty(deptCode)
                ? deptCode.ToLowerInvariant()
                : service.DepartmentId?.ToString(CultureInfo.InvariantCulture) ?? "all");

        var departmentName = !string.IsNullOrWhiteSpace(service.Department?.Name)
            ? service.Department!.Name
            : "Khoa Khám bệnh";

        var category = ResolveCategory(service, deptCode, specialtyCode);
        var durationMinutes = ResolveDurationMinutes(service, category);
        var durationDescription = $"{durationMinutes} phút thăm khám trực tiếp cùng bác sĩ chuyên khoa";

        var isOnlineBookingActive = service.AllowsOnlineBooking ?? false;
        var (badgeType, badgeLabel) = ResolveBadge(category, isOnlineBookingActive);

        var activePrice = service.ServicePrices
            .Where(p => (!p.EffectiveFrom.HasValue || p.EffectiveFrom.Value <= now)
                     && (!p.EffectiveTo.HasValue || p.EffectiveTo.Value > now))
            .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
            .ThenByDescending(p => p.Id)
            .Select(p => p.PriceAmount)
            .FirstOrDefault() ?? 0m;

        var priceFormatted = FormatVndPrice(activePrice);
        var description = !string.IsNullOrWhiteSpace(service.Description)
            ? service.Description!
            : $"Khám và tư vấn chuyên sâu dịch vụ {service.Name} tại {departmentName}.";

        var (location, tags, purposeSummary, highlightSymptoms, suitableFor) =
            ResolvePresentationMetadata(service, deptCode, specialtyCode, departmentName);

        return new ExaminationServiceDto(
            Id: service.Id.ToString(CultureInfo.InvariantCulture),
            ServiceCode: service.ServiceCode,
            Name: service.Name,
            DepartmentId: departmentId,
            DepartmentName: departmentName,
            Location: location,
            IsOnlineBookingActive: isOnlineBookingActive,
            BadgeType: badgeType,
            BadgeLabel: badgeLabel,
            DurationMinutes: durationMinutes,
            DurationDescription: durationDescription,
            Description: description,
            Tags: tags,
            Price: activePrice,
            PriceFormatted: priceFormatted,
            PriceNote: "Khám ngoại trú • giá niêm yết trực tiếp theo dịch vụ",
            Category: category,
            PreparationNotes: null,
            TargetAudience: null,
            PurposeSummary: purposeSummary,
            HighlightSymptoms: highlightSymptoms,
            SuitableFor: suitableFor,
            StandardSteps: DefaultStandardSteps);
    }

    private static string ResolveCategory(Service service, string deptCode, string specialtyCode)
    {
        var name = service.Name ?? string.Empty;
        var code = service.ServiceCode ?? string.Empty;

        if (name.Contains("gói", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("tầm soát", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("PKG", StringComparison.OrdinalIgnoreCase))
        {
            return "package";
        }

        if (name.Contains("nhi", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(deptCode, "PED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(specialtyCode, "PED", StringComparison.OrdinalIgnoreCase))
        {
            return "pediatric";
        }

        if (name.Contains("tổng quát", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(deptCode, "OPD", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(specialtyCode, "GEN", StringComparison.OrdinalIgnoreCase))
        {
            return "general";
        }

        return "specialty";
    }

    private static int ResolveDurationMinutes(Service service, string category)
    {
        if (string.Equals(category, "package", StringComparison.OrdinalIgnoreCase))
            return 60;

        var name = service.Name ?? string.Empty;
        var code = service.ServiceCode ?? string.Empty;

        if (code.EndsWith("-02", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("tái khám", StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        return 25;
    }

    private static (string? BadgeType, string? BadgeLabel) ResolveBadge(string category, bool isOnlineBookingActive)
    {
        if (string.Equals(category, "pediatric", StringComparison.OrdinalIgnoreCase))
            return ("pediatric", "Thân thiện trẻ nhỏ");

        if (isOnlineBookingActive)
            return ("online", "Đặt online nhanh");

        return (null, null);
    }

    private static string FormatVndPrice(decimal price)
    {
        var truncated = decimal.Truncate(price);
        var formattedNumber = truncated.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');
        return $"{formattedNumber} VNĐ";
    }

    private static (
        string Location,
        IReadOnlyList<string> Tags,
        string PurposeSummary,
        IReadOnlyList<string> HighlightSymptoms,
        string SuitableFor) ResolvePresentationMetadata(
        Service service,
        string deptCode,
        string specialtyCode,
        string departmentName)
    {
        var isFollowUp = service.ServiceCode.EndsWith("-02", StringComparison.OrdinalIgnoreCase) ||
                         service.Name.Contains("tái khám", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(deptCode, "GI", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(specialtyCode, "GI", StringComparison.OrdinalIgnoreCase))
        {
            return (
                Location: "Khu Khám Ngoại trú - Tầng 1, Khu Demo (Phòng 101-102)",
                Tags: isFollowUp
                    ? ["Tái khám chuyên khoa", "Theo dõi đáp ứng điều trị"]
                    : ["Bác sĩ CKII", "Tư vấn chế độ ăn"],
                PurposeSummary:
                    "Thăm khám, chẩn đoán phân biệt và tư vấn phác đồ điều trị chuyên khoa đối với các trường hợp: Đau tức vùng thượng vị, trào ngược dạ dày thực quản (GERD), ợ hơi, ợ chua, rối loạn tiêu hóa kéo dài, viêm đại tràng co thắt và bệnh lý gan mật.",
                HighlightSymptoms:
                    ["Đau tức vùng thượng vị", "trào ngược dạ dày thực quản (GERD)", "ợ hơi, ợ chua", "rối loạn tiêu hóa kéo dài"],
                SuitableFor:
                    "Phù hợp cho người bệnh có triệu chứng tiêu hóa - gan mật cần khám trực tiếp cùng bác sĩ chuyên khoa tại phòng khám ngoại trú.");
        }

        if (string.Equals(deptCode, "CARD", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(specialtyCode, "CARD", StringComparison.OrdinalIgnoreCase))
        {
            return (
                Location: "Khu Khám Ngoại trú - Tầng 1, Khu Demo (Phòng 201-202)",
                Tags: isFollowUp
                    ? ["Tái khám tim mạch", "Điều chỉnh phác đồ"]
                    : ["Đo điện tim cơ bản", "Bác sĩ chuyên khoa"],
                PurposeSummary:
                    "Thăm khám và theo dõi các bệnh lý tim mạch: Tăng huyết áp, đau tức ngực khi gắng sức, rối loạn nhịp tim, khó thở về đêm.",
                HighlightSymptoms:
                    ["Tăng huyết áp", "đau tức ngực", "rối loạn nhịp tim", "khó thở"],
                SuitableFor:
                    "Phù hợp cho người bệnh có tiền sử tim mạch hoặc xuất hiện triệu chứng hồi hộp, đau thắt ngực cần khám chuyên khoa.");
        }

        return (
            Location: "Khu Khám Ngoại trú - Tầng 1, Khu Demo",
            Tags: ["Bác sĩ chuyên khoa", "Đặt lịch trực tuyến"],
            PurposeSummary: $"Thăm khám, tư vấn và theo dõi chuyên khoa tại {departmentName} cho dịch vụ {service.Name}.",
            HighlightSymptoms: ["Khám chuyên khoa ngoại trú", "Tư vấn phác đồ điều trị"],
            SuitableFor: $"Phù hợp cho người bệnh có nhu cầu thăm khám trực tiếp tại {departmentName}.");
    }
}
