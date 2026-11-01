# 0006 — Hợp đồng API và quy tắc hủy lịch hẹn (`POST /api/v1/appointments/{idOrCode}/cancel`)

- Ngay: 2026-04-19
- Trang thai: accepted
- Lane: high-risk

## Context
Use case UC14 (Cancel Appointment — M2-006 / #58) cho phép bệnh nhân đã đăng nhập hủy lịch hẹn còn đủ điều kiện của bản thân hoặc người thân đã xác thực liên kết (`VERIFIED`), kèm theo lý do hủy lịch bắt buộc.

Frontend (#57) đã cố định hợp đồng trong `medflow-ai-frontend/src/features/appointment/api/appointment-detail.api.ts:143-179`, `schemas/appointment-detail.schema.ts:15-21`, và `hooks/use-appointment-detail.ts:32-39`:
- Gọi `POST /api/v1/appointments/{appointmentId}/cancel` với body `{ "cancellationReason": string }` (sau khi `trim()`, độ dài từ `10` đến `250` ký tự).
- Phản hồi `200 OK` phải trả về đối tượng `AppointmentDetailDto` đầy đủ để Frontend ghi đè trực tiếp vào cache chi tiết lịch hẹn (`queryClient.setQueryData`) và làm mới danh sách lịch hẹn.

## Decision
1. **Hợp đồng HTTP (`POST /api/v1/appointments/{idOrCode}/cancel`)**:
   - Yêu cầu xác thực JWT với role `PATIENT` (layer 1 authz `[Authorize(Roles = "PATIENT")]`).
   - Tham số đường dẫn `idOrCode` chấp nhận `Id` số nguyên dương hoặc `AppointmentCode` (có thể kèm tiền tố `#`), dùng chung bộ phân tích `AppointmentReadModel.ParseIdentifier` với endpoint xem chi tiết.
   - Request body: `CancelAppointmentRequest { string? CancellationReason }`.
   - Response `200 OK`: `AppointmentDetailDto` được nạp lại qua `IAppointmentDetailService.GetMyAppointmentDetailAsync` sau khi commit transaction, đảm bảo `Status = "CANCELLED"`, `CancellationReason` bằng lý do đã trim, `AvailableActions = ["REBOOK"]`, `PaymentStatus = "REFUNDED_OR_FREE"`.
2. **Thứ tự kiểm tra và mã trạng thái lỗi (`ProblemDetails`)**:
   - `401 Unauthorized` (`UnauthorizedAccessException`): `userId <= 0` hoặc token không hợp lệ.
   - `400 Bad Request` (`ValidationException`):
     - `idOrCode` không hợp lệ (`"Mã lịch hẹn không hợp lệ."`).
     - `cancellationReason` sau `Trim()` bị rỗng hoặc `< 10` ký tự (`"Vui lòng nhập lý do hủy lịch tối thiểu 10 ký tự."`), hoặc `> 250` ký tự (`"Lý do hủy lịch không được vượt quá 250 ký tự."`).
   - `404 Not Found` (`NotFoundException`): không tìm thấy lịch hẹn theo `Id` hoặc `AppointmentCode`.
   - `403 Forbidden` (`ForbiddenException`): `PatientId` của lịch hẹn không thuộc phạm vi sở hữu `PatientAccessScope` (bản thân hoặc thành viên gia đình `VERIFIED`, chưa thu hồi, còn hiệu lực) → `"Bạn không có quyền hủy lịch hẹn này."` (layer 2 authz).
   - `404 Not Found` (`NotFoundException`): bản ghi lịch hẹn thiếu thông tin `Slot`, `SlotId`, `StartsAt` hoặc `EndsAt`.
   - `409 Conflict` (`ConflictException`) — kiểm tra sau khi acquire lock và đọc lại trạng thái hiệu lực (`ResolveEffectiveStatus(tracked, latestVisit)`):
     - Trạng thái hiệu lực đã là `CANCELLED` → `"Lịch hẹn đã được hủy trước đó."`.
     - Trạng thái hiệu lực không thuộc nhóm cho phép chỉnh sửa (`!AppointmentReadModel.IsModifiableStatus(status)`, tức ngoài `BOOKED | CONFIRMED | PENDING`) → `"Lịch hẹn ở trạng thái hiện tại không thể hủy."`.
   - `400 Bad Request` (`ValidationException`): thời điểm bắt đầu slot không thỏa cửa sổ hủy tối thiểu `!AppointmentReadModel.IsWithinCancelWindow(startsAt, nowLocal, policy.CancelMinimumHours)` (`startsAt <= nowLocal || startsAt < nowLocal.AddHours(policy.CancelMinimumHours)`) → `"Chỉ được hủy lịch trước giờ khám tối thiểu {policy.CancelMinimumHours} giờ."`. Dùng chung biểu thức với điều kiện hiển thị nút `CANCEL` trong `AppointmentDetailService.BuildAvailableActionsAsync`.
3. **Lưu trữ DB (Migration 008)**:
   - Thêm 3 cột vào bảng `appointments` qua `database/migrations/008_add_appointments_cancellation_fields.sql` (idempotent) và đồng bộ vào `database/medflow_schema_v4_1.sql`:
     - `cancellation_reason text`
     - `cancelled_at timestamp without time zone` (giờ địa phương Việt Nam UTC+7, naive, đồng nhất với `booked_at`)
     - `cancelled_by_user_id bigint` kèm khóa ngoại `fk_appointments_cancelled_by_user_id` tham chiếu `users(id)`.
4. **Khóa đồng thời (Concurrency Lock) và giải phóng sức chứa Slot**:
   - Bên trong transaction, gọi `IAppointmentBookingLock.AcquireAsync(patientId, null, slotId)` để khóa tuần tự dòng `patients` và `examination_slots` theo đúng thứ tự của luồng đặt lịch, ngăn hai yêu cầu hủy song song hoặc xung đột với kiểm tra sức chứa khi đặt lịch.
   - Không chỉnh sửa bảng `examination_slots`: toàn bộ truy vấn kiểm tra sức chứa (`AppointmentBookingService`, `AppointmentSlotService`) đều đếm các lịch hẹn có `Status == null || Status.ToUpper() != "CANCELLED"`, nên việc cập nhật `Status = "CANCELLED"` tự động trả lại 1 chỗ trống vào pool của slot.
5. **Chính sách Audit Log và bảo vệ PHI**:
   - Ghi một bản ghi `audit_logs` với `action = "APPOINTMENT_CANCEL"`, `entity_type = "APPOINTMENT"`, `entity_id = appointment.Id`, và `change_data` gồm `{ AppointmentCode, PatientId, SlotId, PreviousStatus, NewStatus }`.
   - Không lưu `cancellation_reason` vào `change_data` của `audit_logs` để tránh nhân bản văn bản tự do có thể chứa thông tin sức khỏe nhạy cảm (PHI) sang bảng nhật ký hệ thống.
6. **Hiển thị `CancellationReason` trên Read Model (cập nhật mục 2 của ADR 0005)**:
   - Tại `AppointmentReadModel.MapItem`, trường `CancellationReason` trả về `a.CancellationReason` khi trạng thái hiệu lực là `"CANCELLED"`, và trả về `null` cho mọi trạng thái khác (thay thế quy định tạm thời trả `null` tại mục 2 của ADR 0005 đối với `cancellationReason`).

## Consequences
- Duoc:
  - Luồng hủy lịch hẹn hoạt động đầu-cuối với Frontend hiện có mà không cần sửa code giao diện; bộ quy tắc điều kiện hủy đồng nhất tuyệt đối giữa nút hành động trên màn chi tiết và bước xác nhận hủy.
  - Sức chứa slot được hoàn trả tức thì cho các bệnh nhân khác đặt lịch dưới cơ chế khóa hàng (`SELECT ... FOR UPDATE`) an toàn đồng thời.
- Mat:
  - Bảng `appointments` tăng thêm 3 cột nullable phục vụ lưu vết hủy lịch.
- Rui ro con lai:
  - Việc gửi thông báo sau khi hủy lịch chưa thực hiện tại use case này và sẽ được xử lý tập trung ở UC17 (Notification).
