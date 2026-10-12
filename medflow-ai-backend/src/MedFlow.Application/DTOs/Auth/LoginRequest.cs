using System.ComponentModel.DataAnnotations;

namespace MedFlow.Application.DTOs.Auth;

public class LoginRequest
{
    [Required(ErrorMessage = "Email hoặc tên đăng nhập là bắt buộc")]
    public string Identifier { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu là bắt buộc")]
    public string Password { get; set; } = string.Empty;
}
