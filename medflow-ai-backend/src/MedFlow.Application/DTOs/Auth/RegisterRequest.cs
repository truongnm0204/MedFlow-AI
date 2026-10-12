using System.ComponentModel.DataAnnotations;

namespace MedFlow.Application.DTOs.Auth;

public class RegisterRequest
{
    [Required(ErrorMessage = "Email là bắt buộc")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu là bắt buộc")]
    [MinLength(8, ErrorMessage = "Mật khẩu tối thiểu 8 ký tự")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$",
        ErrorMessage = "Mật khẩu cần có chữ hoa, chữ thường và số")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Xác nhận mật khẩu là bắt buộc")]
    [Compare("Password", ErrorMessage = "Mật khẩu xác nhận không khớp")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Họ tên là bắt buộc")]
    [MaxLength(255)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ngày sinh là bắt buộc")]
    public DateOnly DateOfBirth { get; set; }

    [Required(ErrorMessage = "Giới tính là bắt buộc")]
    [MaxLength(20)]
    public string Gender { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại là bắt buộc")]
    [RegularExpression(@"^0[35789]\d{8}$", ErrorMessage = "Số điện thoại không hợp lệ")]
    public string Phone { get; set; } = string.Empty;
}
