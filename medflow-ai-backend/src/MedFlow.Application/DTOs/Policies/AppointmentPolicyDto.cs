namespace MedFlow.Application.DTOs.Policies;

/// <summary>
/// Cấu hình chính sách đặt lịch khám đang có hiệu lực của bệnh viện.
/// </summary>
/// <param name="BookingWindowDays">Số ngày tối đa được đặt trước.</param>
/// <param name="BookingLeadHours">Số giờ tối thiểu trước giờ khám mới được đặt.</param>
/// <param name="MaxActiveBookings">Số lịch đang hoạt động tối đa của một bệnh nhân.</param>
/// <param name="RescheduleMinimumHours">Số giờ tối thiểu trước giờ khám mới được đổi.</param>
/// <param name="MaxReschedules">Số lần đổi tối đa trên một lịch.</param>
/// <param name="CancelMinimumHours">Số giờ tối thiểu trước giờ khám mới được hủy.</param>
/// <param name="NoShowGraceMinutes">Số phút trễ sau giờ bắt đầu slot trước khi tính no-show.</param>
/// <param name="NoShowMaxCount">Đủ số lần no-show này trong <paramref name="NoShowLookbackDays"/> ngày thì bị chặn đặt online trong <paramref name="NoShowBlockDays"/> ngày.</param>
/// <param name="NoShowLookbackDays">Số ngày xét lịch sử no-show.</param>
/// <param name="NoShowBlockDays">Số ngày bị chặn đặt online khi đủ ngưỡng no-show (0 = không chặn).</param>
/// <param name="LastUpdatedAt">EffectiveFrom lớn nhất trong các dòng đang hiệu lực, null nếu mọi field đều đang lấy mặc định.</param>
public sealed record AppointmentPolicyDto(
    int BookingWindowDays,
    int BookingLeadHours,
    int MaxActiveBookings,
    int RescheduleMinimumHours,
    int MaxReschedules,
    int CancelMinimumHours,
    int NoShowGraceMinutes,
    int NoShowMaxCount,
    int NoShowLookbackDays,
    int NoShowBlockDays,
    DateTime? LastUpdatedAt);
