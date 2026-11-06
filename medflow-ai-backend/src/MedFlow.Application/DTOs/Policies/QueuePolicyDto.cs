namespace MedFlow.Application.DTOs.Policies;

/// <summary>
/// Cấu hình chính sách điều phối hàng đợi đang có hiệu lực của bệnh viện.
/// </summary>
/// <param name="GraceMinutes">Số phút ân hạn cho bệnh nhân đặt lịch đến trễ trước khi chuyển xuống cuối nhóm.</param>
/// <param name="ExpectedDurationMinutes">Thời lượng khám dự kiến trung bình mỗi lượt (phút).</param>
/// <param name="SafetyBufferMinutes">Khoảng đệm an toàn cộng thêm giữa các lượt khám (phút).</param>
/// <param name="WalkinPriorityRatio">Tỷ lệ điều phối xen kẽ giữa nhóm ưu tiên và nhóm thường.</param>
/// <param name="PublicNumberImmutable">Số thứ tự công khai giữ nguyên sau khi cấp (luôn khóa ở true).</param>
/// <param name="ManualPriorityRequiresVerification">Bắt buộc xác minh khi nhân viên gán cờ ưu tiên thủ công.</param>
/// <param name="LastUpdatedAt">EffectiveFrom lớn nhất trong các dòng đang hiệu lực, null nếu mọi field đều đang lấy mặc định.</param>
public sealed record QueuePolicyDto(
    int GraceMinutes,
    int ExpectedDurationMinutes,
    int SafetyBufferMinutes,
    QueuePriorityRatioDto WalkinPriorityRatio,
    bool PublicNumberImmutable,
    bool ManualPriorityRequiresVerification,
    DateTime? LastUpdatedAt);
