using MedFlow.Application.DTOs.Auth;

namespace MedFlow.Application.Services.Auth;

public interface IAuthService
{
    Task<RegisterResponse> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken ct = default);
    Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct = default);
    Task<RefreshResponse> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken ct = default);
    Task LogoutAsync(Guid userId, string refreshToken, string? ipAddress, CancellationToken ct = default);
    Task<VerifyOtpResponse> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken ct = default);
    Task<ResendOtpResponse> ResendOtpAsync(ResendOtpRequest request, CancellationToken ct = default);
    Task<UserDto> GetMeAsync(Guid userId, CancellationToken ct = default);
}
