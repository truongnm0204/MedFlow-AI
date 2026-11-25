namespace MedFlow.Domain.Constants;

/// <summary>
/// Distinguishes benign refresh-token rotation from explicit/security session revocations (ADR-0059).
/// </summary>
public static class AuthSessionRevokeReasons
{
    public const string Rotated = "ROTATED";
    public const string Logout = "LOGOUT";
    public const string PasswordChanged = "PASSWORD_CHANGED";
    public const string PasswordReset = "PASSWORD_RESET";
    public const string TokenReuse = "TOKEN_REUSE";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string AdminRevoked = "ADMIN_REVOKED";
}
