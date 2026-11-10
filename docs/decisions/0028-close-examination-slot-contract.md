# 0028 — Hợp đồng API đóng và mở lại khung giờ khám (`Close & Reopen Examination Slot` — `#216` / `M2-066` / `UC53`)

- Ngay: 2026-11-10
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#216` (`M2-066 — [M2][BE] Close Examination Slot`, thuộc `UC53 Manage Examination Slots`) yêu cầu cung cấp các API và quy tắc nghiệp vụ cho phép quản trị viên bệnh viện (`HOSPITAL_ADMIN`) hoặc quản trị viên hệ thống (`SYSTEM_ADMIN`) đóng các khung giờ khám không còn khả dụng, xử lý cảnh báo và phương án điều phối khi khung giờ đã có bệnh nhân đặt lịch phụ thuộc (`KEEP_EXISTING` hoặc `RESCHEDULE_REQUIRED`), đóng hàng loạt nhiều khung giờ và mở lại khung giờ đã đóng.

Trên nhánh `develop`:
1. Giao diện Frontend (`#215` / `M2-065`) đã định nghĩa đầy đủ 3 luồng thao tác tại `medflow-ai-frontend/src/features/admin-slots/`:
   - Đóng 1 khung giờ (`SlotCloseModal` -> `closeSlot(id, payload)` với `reasonCode`: `DOCTOR_UNAVAILABLE | EQUIPMENT_MAINTENANCE | SCHEDULE_ADJUSTMENT | EMERGENCY | OTHER`, `note`, `notifyDoctor`, `notifyPatients`, `affectedBookingsAction`: `KEEP_EXISTING | RESCHEDULE_REQUIRED`).
   - Đóng hàng loạt (`SlotBatchCloseModal` -> `batchCloseSlots(ids, payload)`).
   - Mở lại khung giờ (`SlotReopenModal` -> `reopenSlot(id)`).
2. Bảng `examination_slots`, `appointments` và `audit_logs` trong `database/medflow_schema_v4_1.sql` đã có sẵn đầy đủ cột cần thiết (`examination_slots.status`, `appointments.reception_note`, `audit_logs.change_data jsonb`). Không cần tạo SQL migration mới hay thay đổi chữ ký `IApplicationDbContext`.

## Decision

1. **Hợp đồng HTTP (`AdminExaminationSlotsController`)**:
   - Hỗ trợ đồng thời trên cả 3 tiền tố route `/api/v1/admin/examination-slots`, `/api/v1/admin/slots` và `/api/v1/examination-slots`:
     - **Đóng đơn lẻ**: `POST|PATCH|PUT {idOrCode}/close` (`200 OK` -> `CloseExaminationSlotResponseDto`).
     - **Đóng hàng loạt**: `POST batch-close` và `POST close-batch` (`200 OK` -> `BatchCloseExaminationSlotsResponseDto`).
     - **Mở lại khung giờ**: `POST|PATCH|PUT {idOrCode}/reopen` (`200 OK` -> `CloseExaminationSlotResponseDto`).
   - `{idOrCode}` chấp nhận ID số nguyên dương (`1`, `"1"`) hoặc mã định dạng (`"SLT-001"`, `"SLT-HN-20261027-01"`).

2. **Phân quyền hai lớp (`401` / `403` / `404`)**:
   - **Lớp 1**: `[Authorize(Roles = "HOSPITAL_ADMIN,SYSTEM_ADMIN")]` trên `AdminExaminationSlotsController`.
   - **Lớp 2 (`ExaminationSlotAdminService.EnsureAdminAccessAsync` & `EnsureSlotHospitalOwnership`)**:
     - Xác minh `actorUserId > 0` (`401`), tài khoản `User` tồn tại (`404`), `AccountStatus == ACTIVE` (`403`), chưa bị khóa `LockedUntil` (`403`), có vai trò `HOSPITAL_ADMIN` hoặc `SYSTEM_ADMIN` trong DB (`403`).
     - Đối với `HOSPITAL_ADMIN`, bắt buộc có hồ sơ `Staff` đang `ACTIVE` gắn với `Hospital` đang hoạt động và chỉ được phép đóng/mở các khung giờ thuộc đúng `HospitalId` của mình (`slotHospitalId` suy ra từ `Room`, `Doctor`, hoặc `Service`; khác cơ sở trả `403 Forbidden`).

3. **Kiểm tra dữ liệu đầu vào, Cảnh báo Booking phụ thuộc & Chuyển trạng thái (`400` / `409`)**:
   - **Lý do đóng (`ReasonCode` & `Note`)**:
     - Chỉ chấp nhận 1 trong 5 mã chuẩn: `DOCTOR_UNAVAILABLE`, `EQUIPMENT_MAINTENANCE`, `SCHEDULE_ADJUSTMENT`, `EMERGENCY`, `OTHER` (sai mã trả `400 Bad Request`).
     - Khi `ReasonCode == "OTHER"`, bắt buộc nhập chi tiết lý do trong `Note` tối thiểu 5 ký tự sau khi trim (`400 Bad Request`).
   - **Kiểm tra trạng thái hiện tại**:
     - Đóng một khung giờ đã ở trạng thái `CLOSED` trả `409 Conflict`.
   - **Cảnh báo & Điều phối Booking phụ thuộc**:
     - Các lượt đặt hẹn phụ thuộc đang hoạt động là các bản ghi `appointments` của `slot.Id` có `Status == null || (Status != "CANCELLED" && Status != "NO_SHOW")`.
     - Khi `activeBookingsCount > 0`: nếu người gọi truyền `ConfirmUnderstood == false` hoặc không gửi kèm xác nhận (`ConfirmUnderstood == true` hoặc `AffectedBookingsAction` hợp lệ `KEEP_EXISTING`/`RESCHEDULE_REQUIRED`), hệ thống từ chối với `409 Conflict` kèm thông điệp cảnh báo số lượng bệnh nhân đã đặt hẹn.
     - Khi đã xác nhận:
       - `KEEP_EXISTING`: giữ nguyên trạng thái và nội dung các lịch hẹn hiện có để bác sĩ/tiếp đón tiếp tục khám theo lịch, chỉ chuyển `slot.Status = "CLOSED"` nhằm khóa nhận lượt đặt mới.
       - `RESCHEDULE_REQUIRED`: giữ nguyên bản ghi lịch hẹn (tuyệt đối không xóa hoặc tự ý hủy lịch của bệnh nhân) và ghi chú cờ điều phối `[RESCHEDULE_REQUIRED]` vào `Appointment.ReceptionNote` trên các lịch chờ khám (`BOOKED`, `CONFIRMED`, `PENDING`) để bộ phận tiếp đón chủ động liên hệ bệnh nhân dời lịch.
   - **Mở lại khung giờ (`ReopenExaminationSlotAsync`)**:
     - Khung giờ phải đang ở trạng thái `CLOSED` (`409`), dịch vụ/bác sĩ/buồng khám được gán phải đang hoạt động (`409`), và không được trùng khoảng thời gian `[StartsAt, EndsAt)` với bất kỳ khung giờ `OPEN`/`FULL` nào khác của cùng bác sĩ hoặc buồng khám (`409`).
     - Khi mở lại, trạng thái được khôi phục về `"FULL"` (nếu `activeBookingsCount >= capacity`) hoặc `"OPEN"`.

4. **Nhật ký kiểm toán (`AuditLog`) & Khôi phục siêu dữ liệu đóng ca**:
   - Mỗi thao tác đóng hoặc mở lại ghi một bản ghi `AuditLog` (`EntityType = "ExaminationSlot"`, `Action = "EXAMINATION_SLOT_CLOSED"` hoặc `"EXAMINATION_SLOT_REOPENED"`) chứa đầy đủ `reasonCode`, `reasonText`, `closeReason`, `note`, `affectedBookingsAction`, `affectedBookingsCount`, `notifyDoctor`, `notifyPatients`, `closedAt`.
   - `ExaminationSlotAdminItemDto` được bổ sung các trường tùy chọn `ClosedAt`, `CloseReason`, `CloseReasonCode`, `CloseNote`, `AffectedBookingsAction` và tự động khôi phục từ bản ghi `AuditLog` mới nhất khi truy vấn danh sách khung giờ (`GetExaminationSlotListAsync`).

## Consequences

- **Duoc**:
  - Đáp ứng trọn vẹn nghiệp vụ `UC53` (Close / Batch-Close / Reopen Examination Slot) và tương thích hoàn toàn với cả Frontend `#215` lẫn các quy tắc đặt lịch/đổi lịch hiện có (`AppointmentSlotService`, `AppointmentBookingService`, `AppointmentRescheduleService`) mà không cần thay đổi schema DB.
  - Bảo toàn dữ liệu lịch hẹn phụ thuộc của bệnh nhân và cung cấp đầy đủ dấu vết kiểm toán.
- **Mat**:
  - Truy vấn danh sách khung giờ quản trị đọc thêm các dòng `audit_logs` tương ứng với những slot đang `CLOSED` trong trang hiện tại (tối đa `pageSize <= 100` dòng mỗi trang, chi phí rất nhỏ).
- **Rui ro con lai**:
  - Việc gửi thông báo SMS/Email thực tế cho bệnh nhân và bác sĩ khi đóng ca được ghi nhận qua cờ `notifyDoctor`/`notifyPatients` trong phản hồi và `AuditLog`; tích hợp cổng gửi thông báo bất đồng bộ sẽ được mở rộng khi có module Notification.
