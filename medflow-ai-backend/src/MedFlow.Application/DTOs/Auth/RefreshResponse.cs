namespace MedFlow.Application.DTOs.Auth;

public sealed record RefreshResponse(
    string AccessToken,
    int AccessTokenExpiresIn,
    string RefreshToken,
    int RefreshTokenExpiresIn);
