namespace MedFlow.Application.DTOs.Auth;

public sealed record RegisterResponse(
    Guid UserId,
    string Email,
    string Message = "Vui lòng kiểm tra email để xác thực tài khoản");
