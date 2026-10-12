namespace MedFlow.Application.DTOs.Auth;

public sealed record ResendOtpResponse(
    string Message = "Nếu email tồn tại, mã OTP đã được gửi");
