using System.Security.Claims;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Application.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>POST /api/v1/auth/register — Đăng ký bệnh nhân mới.</summary>
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterRequest request, CancellationToken ct)
        => Ok(await _authService.RegisterAsync(request, GetIp(), ct));

    /// <summary>POST /api/v1/auth/login — Đăng nhập, trả access + refresh token.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request, CancellationToken ct)
        => Ok(await _authService.LoginAsync(request, GetIp(), ct));

    /// <summary>POST /api/v1/auth/refresh — Đổi refresh token lấy cặp token mới.</summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<RefreshResponse>> Refresh(
        [FromBody] RefreshRequest request, CancellationToken ct)
        => Ok(await _authService.RefreshAsync(request, GetIp(), ct));

    /// <summary>POST /api/v1/auth/logout — Revoke refresh token hiện tại.</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(
        [FromBody] RefreshRequest request, CancellationToken ct)
    {
        await _authService.LogoutAsync(GetUserId(), request.RefreshToken, GetIp(), ct);
        return NoContent();
    }

    /// <summary>POST /api/v1/auth/verify-otp — Xác thực OTP đăng ký.</summary>
    [HttpPost("verify-otp")]
    [EnableRateLimiting("otp")]
    public async Task<ActionResult<VerifyOtpResponse>> VerifyOtp(
        [FromBody] VerifyOtpRequest request, CancellationToken ct)
        => Ok(await _authService.VerifyOtpAsync(request, ct));

    /// <summary>POST /api/v1/auth/resend-otp — Gửi lại OTP (throttle 45s).</summary>
    [HttpPost("resend-otp")]
    [EnableRateLimiting("otp")]
    public async Task<ActionResult<ResendOtpResponse>> ResendOtp(
        [FromBody] ResendOtpRequest request, CancellationToken ct)
        => Ok(await _authService.ResendOtpAsync(request, ct));

    /// <summary>GET /api/v1/auth/me — Thông tin user hiện tại.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
        => Ok(await _authService.GetMeAsync(GetUserId(), ct));

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("Token không hợp lệ");
        return Guid.Parse(sub);
    }

    private string? GetIp()
        => Request.Headers.TryGetValue("X-Forwarded-For", out var ff)
            ? ff.ToString().Split(',')[0].Trim()
            : HttpContext.Connection.RemoteIpAddress?.ToString();
}
