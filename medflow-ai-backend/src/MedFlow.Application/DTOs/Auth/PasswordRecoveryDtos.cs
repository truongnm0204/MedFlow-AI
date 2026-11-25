using System.ComponentModel.DataAnnotations;

namespace MedFlow.Application.DTOs.Auth;

public sealed class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email là bắt buộc")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    public string Email { get; set; } = string.Empty;
}

public sealed record ForgotPasswordResponse(
    string Message = ForgotPasswordResponse.DefaultAntiEnumerationMessage)
{
    public const string DefaultAntiEnumerationMessage =
        "Nếu email đã được đăng ký và đủ điều kiện, mã OTP khôi phục mật khẩu sẽ được gửi đến email của bạn.";
}

public sealed class ResetPasswordRequest
{
    [Required(ErrorMessage = "Email là bắt buộc")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã OTP là bắt buộc")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Mã OTP phải gồm 6 chữ số")]
    public string OtpCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu mới là bắt buộc")]
    [MinLength(8, ErrorMessage = "Mật khẩu tối thiểu 8 ký tự")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$",
        ErrorMessage = "Mật khẩu cần có chữ hoa, chữ thường và số")]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed record ResetPasswordResponse(
    string Message = "Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới.");

public sealed class ChangePasswordRequest
{
    [Required(ErrorMessage = "Mật khẩu hiện tại là bắt buộc")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu mới là bắt buộc")]
    [MinLength(8, ErrorMessage = "Mật khẩu tối thiểu 8 ký tự")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$",
        ErrorMessage = "Mật khẩu cần có chữ hoa, chữ thường và số")]
    public string NewPassword { get; set; } = string.Empty;

    public string? CurrentRefreshToken { get; set; }
}

public sealed record ChangePasswordResponse(
    string Message = "Đổi mật khẩu thành công.",
    bool KeptCurrentSession = false);
