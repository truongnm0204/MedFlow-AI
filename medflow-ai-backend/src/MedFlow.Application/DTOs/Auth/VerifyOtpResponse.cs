namespace MedFlow.Application.DTOs.Auth;

public sealed record VerifyOtpResponse(
    Guid UserId,
    string Message = "Xác thực thành công");
