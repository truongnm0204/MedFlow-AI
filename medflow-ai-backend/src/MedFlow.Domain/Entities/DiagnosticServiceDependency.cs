namespace MedFlow.Domain.Entities;

/// <summary>
/// Điều kiện phụ thuộc chuyên môn giữa hai dịch vụ CLS (#869).
/// <see cref="ServiceId"/> chỉ được thực hiện sau <see cref="PrerequisiteServiceId"/> trong cùng lượt khám.
/// </summary>
public class DiagnosticServiceDependency
{
    public long Id { get; set; }
    public long? HospitalId { get; set; }
    public long ServiceId { get; set; }
    public long PrerequisiteServiceId { get; set; }
    public string? Note { get; set; }
    public DateTime? CreatedAt { get; set; }
}
