namespace MedFlow.Domain.Constants;

/// <summary>
/// JWT scope claims (plan v2.1 mục 3.1 — registration session giới hạn quyền).
/// A token carrying <see cref="EkycRegistration"/> is a registration-scope token: it may verify
/// email status, call eKYC endpoints and read own account status ONLY — never patient/appointment/
/// medical-record APIs. Enforcement: <c>RegistrationScopeMiddleware</c> in the API layer rejects
/// any scoped request outside the whitelist with 403. Absence of the claim = full scope (normal
/// patient/staff tokens unchanged).
/// </summary>
public static class AuthTokenScopes
{
    /// <summary>JWT claim type carrying the token scope.</summary>
    public const string ClaimType = "scope";

    /// <summary>
    /// Registration-in-progress scope: whitelist = /api/v1/ekyc/*, /api/v1/auth/me,
    /// /api/v1/auth/logout, /api/v1/auth/verify-otp, /api/v1/auth/resend-otp.
    /// </summary>
    public const string EkycRegistration = "ekyc-registration";
}
