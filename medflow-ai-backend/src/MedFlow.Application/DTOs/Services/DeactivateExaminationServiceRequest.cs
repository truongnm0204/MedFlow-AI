namespace MedFlow.Application.DTOs.Services;

/// <summary>
/// Yêu cầu ngừng hoạt động dịch vụ khám (UC50 / #196).
/// </summary>
/// <param name="ReasonCode">Mã lý do chuẩn hóa (ví dụ: DISCONTINUED, POLICY_CHANGE, TEMPORARY_SUSPENSION, OTHER).</param>
/// <param name="Reason">Lý do chi tiết ngừng hoạt động.</param>
/// <param name="Note">Ghi chú bổ sung từ quản trị viên.</param>
/// <param name="ConfirmUnderstood">Xác nhận đã hiểu và chấp nhận ngừng hoạt động dịch vụ khi còn phụ thuộc.</param>
/// <param name="AffectedBookingsAction">Phương án xử lý lịch khám tương lai (CLOSE_FUTURE_SLOTS, KEEP_EXISTING).</param>
/// <param name="CloseFutureSlots">Cờ tự động đóng tất cả khung giờ khám chưa diễn ra của dịch vụ.</param>
public sealed record DeactivateExaminationServiceRequest(
    string? ReasonCode = null,
    string? Reason = null,
    string? Note = null,
    bool ConfirmUnderstood = false,
    string? AffectedBookingsAction = null,
    bool CloseFutureSlots = false
);
