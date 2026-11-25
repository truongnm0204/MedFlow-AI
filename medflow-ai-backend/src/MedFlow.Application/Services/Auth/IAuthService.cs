using MedFlow.Application.DTOs.Auth;

namespace MedFlow.Application.Services.Auth;

public interface IAuthService
{
    Task<RegisterResponse> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken ct = default);
    Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct = default);
    Task<RefreshResponse> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken ct = default);
    Task LogoutAsync(long userId, string refreshToken, string? ipAddress, CancellationToken ct = default);
    Task LogoutAsync(
        long userId,
        string refreshToken,
        long? currentSessionId,
        string? ipAddress,
        CancellationToken ct = default)
        => LogoutAsync(userId, refreshToken, ipAddress, ct);
    Task<VerifyOtpResponse> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken ct = default);
    Task<ResendOtpResponse> ResendOtpAsync(ResendOtpRequest request, CancellationToken ct = default);
    Task<ForgotPasswordResponse> ForgotPasswordAsync(ForgotPasswordRequest request, string? ipAddress, CancellationToken ct = default);
    Task<ResetPasswordResponse> ResetPasswordAsync(ResetPasswordRequest request, string? ipAddress, CancellationToken ct = default);
    Task<ChangePasswordResponse> ChangePasswordAsync(
        long userId,
        ChangePasswordRequest request,
        long? currentSessionId,
        string? ipAddress,
        CancellationToken ct = default);
    Task<UserDto> GetMeAsync(long userId, CancellationToken ct = default);
}
