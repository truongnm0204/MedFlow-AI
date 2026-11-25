# 0055 — Schedule Background Jobs, Partial Indexes & Patient Notifications Contract (`UC17` / `#824`, `#62`)

- Ngày: 2026-11-25
- Trạng thái: accepted
- Lane: high-risk
- Liên quan: `0001`, `0003`, `0004`, `0006`, `0016`, `0024`, `0028`, `0050`
- Issues: `#824`, `#62`

## Bối cảnh

Hệ thống MedFlow AI cần khép kín vòng đời tự động của lịch hẹn khám ngoại trú và kênh thông báo cho bệnh nhân (`UC17 — Receive Notifications and Reminders`):
1. Phát sinh thông báo tức thì (in-app và hàng đợi email) khi Đặt lịch (`UC12`), Đổi lịch (`UC13`) và Hủy lịch (`UC14`).
2. Quét định kỳ nhắc lịch trước giờ khám theo cấu hình bệnh viện (`reminder_hours`), tự động ghi nhận vắng mặt (`NO_SHOW`) khi quá `slot.ends_at + no_show_grace_minutes` đồng bộ với hàng đợi đặt trước (`queue_entries`), và bù cuốn chiếu các khung giờ khám tương lai (`examination_slots`).
3. Đảm bảo an toàn toàn vẹn dữ liệu khi chạy nhiều container song song trên cùng cơ sở dữ liệu PostgreSQL mà không cần bổ sung hạ tầng ngoài (Hangfire/Quartz/Redis).

## Quyết định

### 1. Toàn vẹn Khóa ngoại, Cột Mở rộng & Bộ Partial / Unique Indexes (`Migration 020`)

- **Giữ nguyên hành vi khóa ngoại `NO ACTION` (`DeleteBehavior.Restrict`)** trên cả ba khóa ngoại của bảng `notifications` (`fk_notifications_patient_id`, `fk_notifications_appointment_id`, `fk_notifications_visit_id`) nhằm bảo vệ toàn vẹn tham chiếu lịch sử trên Production. Các bài kiểm thử tích hợp có xóa dữ liệu tạm sử dụng helper dọn `notifications` tường minh trước khi xóa `appointments`.
- **Mở rộng bảng `notifications`**:
  - `read_at` (`timestamp without time zone NULL`): thời điểm bệnh nhân đọc thông báo trên cổng thông tin.
  - `notification_type` (`varchar(40) NULL`): phân loại sự kiện (`APPOINTMENT_BOOKED`, `APPOINTMENT_RESCHEDULED`, `APPOINTMENT_CANCELLED`, `APPOINTMENT_REMINDER`, `APPOINTMENT_NO_SHOW`).
  - `processing_token` (`varchar(64) NULL`) và `lease_until` (`timestamp without time zone NULL`): quản lý quyền sở hữu tạm thời của worker gửi email, tách biệt hoàn toàn khỏi ngữ nghĩa thời điểm dự kiến gửi `scheduled_at`.
- **Bộ Partial & Unique Indexes**:
  - `ix_notifications_pending_due` (`(scheduled_at) WHERE status = 'PENDING'`) và `ix_notifications_processing_lease` (`(lease_until) WHERE status = 'PROCESSING'`).
  - `uq_notifications_idempotency_key` (`UNIQUE (idempotency_key) WHERE idempotency_key IS NOT NULL`).
  - `ix_notifications_patient_unread` (`(patient_id, created_at DESC) WHERE read_at IS NULL`).
  - `ix_appointments_active_slot` (`(slot_id) WHERE status IN ('BOOKED', 'CONFIRMED')`) và `ix_examination_slots_time_window` (`(starts_at, ends_at)`).
  - Bốn Unique Index bảo vệ chống trùng khung giờ khám trên `examination_slots`:
    - `uq_examination_slots_active_doctor_start`: `UNIQUE (doctor_id, starts_at) WHERE doctor_id IS NOT NULL AND status <> 'CLOSED'` (ngăn một bác sĩ có nhiều slot hoạt động ở các dịch vụ khác nhau tại cùng một thời điểm).
    - `uq_examination_slots_active_room_start`: `UNIQUE (room_id, starts_at) WHERE room_id IS NOT NULL AND status <> 'CLOSED'` (ngăn xung đột buồng khám tại cùng thời điểm).
    - `uq_examination_slots_doctor_service_start`: `UNIQUE (doctor_id, service_id, starts_at) WHERE doctor_id IS NOT NULL AND service_id IS NOT NULL` (ngăn sinh lại slot đè lên khung giờ đã bị Quản trị viên đóng `CLOSED`).
    - `uq_examination_slots_unassigned_service_room_start`: `UNIQUE (service_id, room_id, starts_at) WHERE doctor_id IS NULL AND service_id IS NOT NULL AND room_id IS NOT NULL`.

### 2. Hàng đợi Email Outbox theo Mô hình At-least-once (`NotificationOutboxProcessor`)

- Chạy chu kỳ `30s` (`ScheduleJobsOptions.OutboxIntervalSeconds`), tắt vòng lặp nền mặc định trong môi trường `Test` (`ScheduleJobs:Enabled = false`).
- **Claim theo lô & Tách rời I/O**: Dùng `FOR UPDATE SKIP LOCKED` chuyển tối đa `BatchSize` dòng `PENDING` đến hạn sang `PROCESSING` kèm `processing_token` mới và `lease_until = nowLocal + ProcessingLeaseSeconds` (`120s`), đóng kết nối DB ngay lập tức trước khi gọi `IEmailSender` (`SemaphoreSlim(5)`, timeout SMTP `20s < 120s`).
- **Guarded Finalization Update**: Khi cập nhật `SENT`, tăng `retry_count` (tối đa 3 lần với exponential backoff) hoặc chuyển `FAILED`/`CANCELLED`, bắt buộc khớp `WHERE id = @id AND status = 'PROCESSING' AND processing_token = @token` để ngăn worker bị treo ghi đè kết quả của worker mới sau khi hết hạn lease.
- **Xác thực người nhận & Chống gửi nhắc lịch lỗi thời**:
  - Kênh `EMAIL` có địa chỉ trống hoặc sai định dạng bị chuyển ngay sang `FAILED` (không bao giờ đánh dấu `SENT`). Hồ sơ không có email sử dụng `Channel = 'IN_APP'`, `Status = 'DELIVERED'`.
  - Khi Đổi lịch hoặc Hủy lịch, các bản ghi reminder `PENDING` của lịch cũ bị chuyển sang `CANCELLED` ngay trong cùng giao dịch; đồng thời worker kiểm tra lại trạng thái `Appointment` còn chờ khám hợp lệ ngay trước khi gửi SMTP.
- Chấp nhận rõ ràng đặc tính **At-least-once Delivery**: trong trường hợp hiếm khi máy chủ SMTP đã nhận thư nhưng tiến trình worker bị sập trước khi kịp ghi `SENT`, bản ghi sẽ được gửi lại sau khi `lease_until` hết hạn.

### 3. Vòng đời Lịch hẹn: Nhắc lịch, Chốt `NO_SHOW` Đồng bộ `QueueEntry` & Khóa Check-in (`AppointmentLifecycleProcessor`)

- **Nhắc lịch hẹn (`UC17`)**: Đọc `reminder_hours` (`hours_before`, mặc định `24` giờ) từ `hospital_policies` (`NOTIFICATION`), sinh thông báo cho các lịch `BOOKED`/`CONFIRMED` chưa có `Visit` trong khoảng `(nowLocal, nowLocal + reminderHours]` với khóa chống trùng gắn theo phiên bản khung giờ `appt-reminder:{appointmentId}:{slotId}:{startsAt:yyyyMMddHHmm}`.
- **Tự động chốt `NO_SHOW` đồng bộ hàng đợi**:
  - Đọc `NoShowGraceMinutes` (mặc định `15` phút) từ `IAppointmentPolicyService`.
  - Khóa dòng `appointments` bằng `FOR UPDATE SKIP LOCKED`, xác minh lại chưa phát sinh `Visit` khác `CANCELLED` và chưa có `QueueEntry` đang phục vụ (`WAITING`/`READY`/`CALLED`/`IN_PROGRESS`/`COMPLETED`).
  - Trong cùng một giao dịch: chuyển `Appointment.Status = 'NO_SHOW'`; chuyển các `QueueEntry` đặt trước (`RESERVED`) liên kết sang `Status = 'CANCELLED'` (giữ nguyên `PresenceStatus = 'NOT_ARRIVED'`, tăng `StateVersion`, **giữ nguyên không tái sử dụng `QueueNumber`**); ghi `AuditLog` (`APPOINTMENT_AUTO_NO_SHOW` và `QUEUE_ENTRY_CANCELLED`); phát sinh thông báo `appt-noshow:{appointmentId}:{slotId}`.
- **Loại trừ tranh chấp với Lễ tân Check-in (`UC24`)**: `CheckInScheduledPatientService` khóa `SELECT 1 FROM appointments WHERE id = @id FOR UPDATE` ngay khi mở giao dịch và kiểm tra lại trạng thái trước khi chuyển `CHECKED_IN` + tạo `Visit`.

### 4. Sinh Cuốn chiếu Khung giờ Khám (`RollingExaminationSlotGenerator`)

- Chạy khi khởi động và định kỳ mỗi `6h`, đọc `booking_window_days` (mặc định `14` ngày khi chưa cấu hình), `slot_duration_minutes` (`30` phút), `slot_capacity` (`3`) từ `hospital_policies` và tuân thủ `HospitalShiftRules`.
- Chỉ chiếu cuốn chiếu từ cấu hình lịch trực hoặc mẫu ca trực hợp lệ thực tế của bác sĩ đang `ACTIVE` (cùng khoa, phòng và dịch vụ `ACTIVE`); bác sĩ chưa có lịch làm việc hợp lệ không bị tự động sinh lịch.
- Kiểm tra không chồng lấn khoảng thời gian của bác sĩ/buồng khám (kể cả các slot `CLOSED`, không bao giờ tự mở lại slot đã đóng) và sử dụng `INSERT ... ON CONFLICT DO NOTHING`.

### 5. Hợp đồng API Thông báo & Cô lập Cache Frontend (`UC17`)

- **Endpoints (`NotificationsController`)**:
  - `GET /api/v1/notifications`: trả `unreadCount`, `items` và `activeReminder` (thuộc phạm vi bản thân + thành viên gia đình `VERIFIED` qua phân quyền 2 lớp). `activeReminder` được tính trực tiếp từ trạng thái `Appointment` đang hiệu lực trong cửa sổ nhắc hẹn, độc lập với `Notification.ReadAt`.
  - `POST|PATCH /api/v1/notifications/{id:long}/read` và `POST /api/v1/notifications/read-all`.
- **Frontend**: `NotificationBellPopover` và `AppointmentReminderBanner` nối trực tiếp vào API thật (xóa bỏ dữ liệu mẫu tĩnh); cache TanStack Query phân tách theo `['notifications', userId]`, có rollback khi lỗi mutation và tự động xóa sạch cache khi đăng xuất hoặc đổi tài khoản.

## Hệ quả

- **Tích cực**: Đảm bảo tính nhất quán giữa `appointments`, `queue_entries`, `examination_slots` và `notifications` dưới tải đồng thời nhiều worker/container trên PostgreSQL; bảo toàn ràng buộc khóa ngoại `NO ACTION` và ngăn rò rỉ thông báo giữa các phiên đăng nhập.
- **Đánh đổi**: Giao thức SMTP không hỗ trợ giao dịch hai pha với PostgreSQL nên hệ thống vận hành theo mô hình *At-least-once* có kiểm soát bằng `processing_token` + `lease_until`.
