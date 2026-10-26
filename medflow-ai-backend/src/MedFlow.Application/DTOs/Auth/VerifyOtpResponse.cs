namespace MedFlow.Application.DTOs.Auth;

public sealed record VerifyOtpResponse(
    long UserId,
    string Message = "Xác thực thành công");
