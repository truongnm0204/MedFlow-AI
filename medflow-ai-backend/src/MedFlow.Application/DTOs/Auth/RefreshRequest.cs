using System.ComponentModel.DataAnnotations;

namespace MedFlow.Application.DTOs.Auth;

public class RefreshRequest
{
    [Required(ErrorMessage = "Refresh token là bắt buộc")]
    public string RefreshToken { get; set; } = string.Empty;
}
