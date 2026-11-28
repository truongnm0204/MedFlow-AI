namespace MedFlow.Application.DTOs.Auth;

public sealed record VerifyOtpResponse(
    long UserId,
    string Message = "Xác thực thành công",
    string? AccessToken = null,
    int? AccessTokenExpiresIn = null,
    bool RequiresEkycVerification = false);
