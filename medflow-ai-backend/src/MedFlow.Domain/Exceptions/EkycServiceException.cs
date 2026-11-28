namespace MedFlow.Domain.Exceptions;

/// <summary>
/// Domain failure with a machine-readable <see cref="Code"/> and explicit HTTP status, used by
/// the eKYC flow to tell reception WHY verification could not run (VNPT_UNAVAILABLE /
/// QUOTA_EXHAUSTED) so staff can choose the UNVERIFIED-profile path. Never carries secrets or
/// evidence payloads.
/// </summary>
public class EkycServiceException : DomainException
{
    public EkycServiceException(string code, string message, int statusCode)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }

    public int StatusCode { get; }
}
