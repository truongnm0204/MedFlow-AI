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
    [EnableRateLimiting("auth-register")]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterRequest request, CancellationToken ct)
        => Ok(await _authService.RegisterAsync(request, GetIp(), ct));

    /// <summary>POST /api/v1/auth/login — Đăng nhập, trả access + refresh token.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("auth-login")]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request, CancellationToken ct)
        => Ok(await _authService.LoginAsync(request, GetIp(), ct));

    /// <summary>POST /api/v1/auth/refresh — Đổi refresh token lấy cặp token mới.</summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("auth-refresh")]
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
    [EnableRateLimiting("otp-verify")]
    public async Task<ActionResult<VerifyOtpResponse>> VerifyOtp(
        [FromBody] VerifyOtpRequest request, CancellationToken ct)
        => Ok(await _authService.VerifyOtpAsync(request, ct));

    /// <summary>POST /api/v1/auth/resend-otp — Gửi lại OTP (throttle 45s).</summary>
    [HttpPost("resend-otp")]
    [EnableRateLimiting("otp-resend")]
    public async Task<ActionResult<ResendOtpResponse>> ResendOtp(
        [FromBody] ResendOtpRequest request, CancellationToken ct)
        => Ok(await _authService.ResendOtpAsync(request, ct));

    /// <summary>POST /api/v1/auth/forgot-password — Yêu cầu gửi OTP khôi phục mật khẩu (chống dò tài khoản).</summary>
    [HttpPost("forgot-password")]
    [EnableRateLimiting("otp-resend")]
    [ProducesResponseType<ForgotPasswordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword(
        [FromBody] ForgotPasswordRequest request, CancellationToken ct)
        => Ok(await _authService.ForgotPasswordAsync(request, GetIp(), ct));

    /// <summary>POST /api/v1/auth/reset-password — Đặt lại mật khẩu bằng OTP một lần và thu hồi toàn bộ phiên.</summary>
    [HttpPost("reset-password")]
    [EnableRateLimiting("otp-verify")]
    [ProducesResponseType<ResetPasswordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ResetPasswordResponse>> ResetPassword(
        [FromBody] ResetPasswordRequest request, CancellationToken ct)
        => Ok(await _authService.ResetPasswordAsync(request, GetIp(), ct));

    /// <summary>POST /api/v1/auth/change-password — Đổi mật khẩu khi đã đăng nhập, thu hồi các phiên khác.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType<ChangePasswordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ChangePasswordResponse>> ChangePassword(
        [FromBody] ChangePasswordRequest request, CancellationToken ct)
        => Ok(await _authService.ChangePasswordAsync(GetUserId(), request, GetSessionId(), GetIp(), ct));

    /// <summary>GET /api/v1/auth/me — Thông tin user hiện tại.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
        => Ok(await _authService.GetMeAsync(GetUserId(), ct));

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId) || userId <= 0)
            throw new UnauthorizedAccessException("Token không hợp lệ");
        return userId;
    }

    private long? GetSessionId()
    {
        var sid = User.FindFirstValue("sid");
        return !string.IsNullOrWhiteSpace(sid) && long.TryParse(sid, out var sessionId) && sessionId > 0
            ? sessionId
            : null;
    }

    private string? GetIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
