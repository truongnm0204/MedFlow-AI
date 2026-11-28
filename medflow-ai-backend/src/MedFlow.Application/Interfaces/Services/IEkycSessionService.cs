using MedFlow.Application.DTOs.Ekyc;

namespace MedFlow.Application.Interfaces.Services;

public interface IEkycSessionService
{
    /// <summary>
    /// Creates an INITIATED self-service eKYC session for the authenticated account
    /// (purpose ACCOUNT_ACTIVATION). Requires valid consent (version + acceptance) and a declared
    /// CCCD for OCR matching.
    /// </summary>
    Task<CreateEkycSessionResponse> CreateAsync(
        long actorUserId,
        CreateEkycSessionRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

    /// <summary>
    /// Creates an eKYC session for a patient profile on behalf of reception staff
    /// (mode RECEPTION_ASSISTED, purpose PATIENT_PROFILE). Permission PATIENT_IDENTITY_VERIFY
    /// and staff status are enforced. performed_by records the staff account.
    /// </summary>
    Task<CreateEkycSessionResponse> CreateReceptionAsync(
        long actorUserId,
        CreateReceptionEkycSessionRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

    /// <summary>Returns session status for the owning account or the staff operator — ownership-enforced.</summary>
    Task<EkycSessionStatusResponse> GetStatusAsync(
        long actorUserId,
        long sessionId,
        CancellationToken ct = default);
}

public interface IReceptionPatientProfileService
{
    /// <summary>
    /// Creates a patient profile WITHOUT eKYC for an owner-approved exception reason.
    /// Runs the dedupe matrix first; audits PATIENT_PROFILE_CREATED_UNVERIFIED with the reason.
    /// Never writes VERIFIED.
    /// </summary>
    Task<CreateUnverifiedPatientProfileResponse> CreateUnverifiedAsync(
        long actorUserId,
        CreateUnverifiedPatientProfileRequest request,
        string? ipAddress,
        CancellationToken ct = default);
}
