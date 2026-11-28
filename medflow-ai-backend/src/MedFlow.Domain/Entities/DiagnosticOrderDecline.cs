namespace MedFlow.Domain.Entities;

/// <summary>
/// Ghi nhận bệnh nhân từ chối thực hiện CLS (#877, owner chốt 5a).
/// Reception/bệnh nhân chỉ ghi nhận + lý do; KHÔNG huỷ chỉ định. Chỉ bác sĩ huỷ.
/// </summary>
public class DiagnosticOrderDecline
{
    public const string SourcePatient = "PATIENT";
    public const string SourceReception = "RECEPTION";
    public const string SourceDoctor = "DOCTOR";

    public long Id { get; set; }
    public long DiagnosticOrderId { get; set; }
    public long VisitId { get; set; }
    public long? HospitalId { get; set; }
    public long RecordedByUserId { get; set; }
    public string RecordedByRole { get; set; } = string.Empty;
    public string Source { get; set; } = SourceReception;
    public string Reason { get; set; } = string.Empty;
    public long? NotifiedDoctorId { get; set; }
    public long? NotifiedDoctorUserId { get; set; }
    public DateTime? CreatedAt { get; set; }
}
