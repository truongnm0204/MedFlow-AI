using System.ComponentModel.DataAnnotations;

namespace MedFlow.Application.DTOs.Auth;

public class ResendOtpRequest
{
    [Required(ErrorMessage = "Email là bắt buộc")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    public string Email { get; set; } = string.Empty;
}
