namespace MedFlow.Domain.Constants;

/// <summary>
/// Canonical fine-grained RBAC permission codes matching the database <c>permissions</c> table.
/// </summary>
public static class PermissionCodes
{
    public const string PatientProfileRead = "PATIENT_PROFILE_READ";
    public const string AppointmentBook = "APPOINTMENT_BOOK";
    public const string PatientCheckin = "PATIENT_CHECKIN";
    public const string VisitAssign = "VISIT_ASSIGN";
    public const string QueueManage = "QUEUE_MANAGE";
    public const string MedicalRecordWrite = "MEDICAL_RECORD_WRITE";
    public const string DiagnosticOrder = "DIAGNOSTIC_ORDER";
    public const string DiagnosticResultWrite = "DIAGNOSTIC_RESULT_WRITE";
    public const string PrescriptionIssue = "PRESCRIPTION_ISSUE";
    public const string MedicineDispense = "MEDICINE_DISPENSE";
    public const string HospitalConfig = "HOSPITAL_CONFIG";
    public const string UserManage = "USER_MANAGE";
    public const string AuditRead = "AUDIT_READ";
    public const string PaymentView = "PAYMENT_VIEW";
    public const string PaymentCollect = "PAYMENT_COLLECT";
    public const string PatientIdentityVerify = "PATIENT_IDENTITY_VERIFY";
    public const string QueueEnroll = "QUEUE_ENROLL";
}
