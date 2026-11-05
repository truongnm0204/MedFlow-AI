namespace MedFlow.Domain.Entities;

public class Prescription
{
    public long Id { get; set; }
    public string? PrescriptionCode { get; set; }
    public long? VisitId { get; set; }
    public long? PrescribingDoctorId { get; set; }
    public string? FulfillmentChoice { get; set; }
    public string? Status { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public long? ReplacesPrescriptionId { get; set; }
}
