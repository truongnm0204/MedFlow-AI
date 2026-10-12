namespace MedFlow.Application.DTOs.Auth;

public sealed record LoginResponse(
    string AccessToken,
    int AccessTokenExpiresIn,
    string RefreshToken,
    int RefreshTokenExpiresIn,
    UserDto User);
