namespace MedFlow.Domain.Constants;

/// <summary>
/// Canonical RBAC role codes matching the database <c>roles</c> table.
/// </summary>
public static class RoleNames
{
    public const string Patient = "PATIENT";
    public const string Doctor = "DOCTOR";
    public const string Reception = "RECEPTION";
    public const string DiagnosticStaff = "DIAGNOSTIC_STAFF";
    public const string PharmacyStaff = "PHARMACY_STAFF";
    public const string HospitalAdmin = "HOSPITAL_ADMIN";
    public const string SystemAdmin = "SYSTEM_ADMIN";

    // Optional system role catalog templates
    public const string HeadDoctor = "HEAD_DOCTOR";
    public const string ClinicalNurse = "CLINICAL_NURSE";
    public const string SecurityAuditor = "SECURITY_AUDITOR";
}
