namespace MedFlow.Domain.Entities;

/// <summary>
/// Giao dịch nhận từ cổng thanh toán. Bảng chỉ giữ dữ liệu đối soát tối thiểu,
/// không lưu nội dung chuyển khoản thô (có thể chứa tên bệnh nhân).
/// </summary>
public class PaymentGatewayEvent
{
    public long Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public long? PaymentId { get; set; }
    public string? PaymentCode { get; set; }
    public decimal? TransferAmount { get; set; }
    public string? ReferenceCode { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
}
