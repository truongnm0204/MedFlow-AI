using System.ComponentModel.DataAnnotations;

namespace MedFlow.Application.DTOs.Reception;

public sealed class CreatePatientAtReceptionRequest
{
    [Required(ErrorMessage = "Họ tên là bắt buộc")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Họ tên phải từ 2 đến 200 ký tự")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ngày sinh là bắt buộc")]
    public DateOnly? DateOfBirth { get; set; }

    [Required(ErrorMessage = "Giới tính là bắt buộc")]
    [RegularExpression("^(?i)(MALE|FEMALE|OTHER)$", ErrorMessage = "Giới tính phải là MALE, FEMALE hoặc OTHER")]
    public string Gender { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại là bắt buộc")]
    [RegularExpression(@"^0[35789]\d{8}$", ErrorMessage = "Số điện thoại không hợp lệ")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Địa chỉ là bắt buộc")]
    [StringLength(500, ErrorMessage = "Địa chỉ tối đa 500 ký tự")]
    public string Address { get; set; } = string.Empty;

    [RegularExpression(@"^\d{12}$", ErrorMessage = "CCCD phải gồm đúng 12 chữ số")]
    public string? IdentityCard { get; set; }

    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    [StringLength(255, ErrorMessage = "Email tối đa 255 ký tự")]
    public string? Email { get; set; }

    [RegularExpression("^(?i)(A|B|AB|O)[+-]$", ErrorMessage = "Nhóm máu phải là A+, A-, B+, B-, AB+, AB-, O+ hoặc O-")]
    public string? BloodType { get; set; }
}
