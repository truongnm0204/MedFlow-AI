using System.ComponentModel.DataAnnotations;

namespace MedFlow.Application.DTOs.Auth;

public class VerifyOtpRequest
{
    [Required(ErrorMessage = "Email là bắt buộc")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã OTP là bắt buộc")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "Mã OTP gồm 6 số")]
    public string Otp { get; set; } = string.Empty;
}
