namespace MedFlow.Domain.Entities;

/// <summary>
/// Lịch sử điều phối điểm CLS (#870). Ghi mỗi lần gán tự động hoặc điều phối lại thủ công
/// kèm lý do và người thực hiện để truy vết/audit.
/// </summary>
public class DiagnosticOrderRoutingHistory
{
    public const string SourceAutoRouting = "AUTO_ROUTING";
    public const string SourceManualReassign = "MANUAL_REASSIGN";
    public const string SourceReceiveReroute = "RECEIVE_REROUTE";

    public long Id { get; set; }
    public long DiagnosticOrderId { get; set; }
    public long? VisitId { get; set; }
    public long? HospitalId { get; set; }
    public long? FromServicePointId { get; set; }
    public long ToServicePointId { get; set; }
    public string Source { get; set; } = SourceAutoRouting;
    public string? Reason { get; set; }
    public long? ChangedByUserId { get; set; }
    public string? ChangedByRole { get; set; }
    public DateTime? CreatedAt { get; set; }
}
