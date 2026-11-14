using System.ComponentModel.DataAnnotations;

namespace MedFlow.Application.DTOs.Reception;

public sealed class UpdatePatientAtReceptionRequest
{
    [Required(ErrorMessage = "Họ tên là bắt buộc.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Họ tên phải từ 2 đến 200 ký tự.")]
    public string FullName { get; init; } = string.Empty;

    public DateOnly? DateOfBirth { get; init; }

    [RegularExpression("^(MALE|FEMALE|OTHER)$", ErrorMessage = "Giới tính phải là MALE, FEMALE hoặc OTHER.")]
    public string? Gender { get; init; }

    [Required(ErrorMessage = "Số điện thoại là bắt buộc.")]
    [RegularExpression(@"^0[35789]\d{8}$", ErrorMessage = "Số điện thoại không hợp lệ.")]
    public string Phone { get; init; } = string.Empty;

    [Required(ErrorMessage = "Địa chỉ là bắt buộc.")]
    [StringLength(500, ErrorMessage = "Địa chỉ tối đa 500 ký tự.")]
    public string Address { get; init; } = string.Empty;

    [RegularExpression(@"^\d{9}(\d{3})?$", ErrorMessage = "Số CCCD/định danh phải gồm 9 hoặc 12 chữ số.")]
    public string? IdentityCard { get; set; }

    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(255, ErrorMessage = "Email tối đa 255 ký tự.")]
    public string? Email { get; init; }

    [StringLength(10, ErrorMessage = "Nhóm máu tối đa 10 ký tự.")]
    public string? BloodType { get; init; }
}
