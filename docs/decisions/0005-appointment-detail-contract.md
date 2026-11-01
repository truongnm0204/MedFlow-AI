# 0005 — Hợp đồng API và quy tắc xem chi tiết lịch hẹn (`GET /api/v1/appointments/{idOrCode}`)

- Ngay: 2026-04-17
- Trang thai: accepted
- Lane: high-risk

## Context
Use case UC15 (View Appointment Detail — M1-54 / #54) cho phép bệnh nhân đã đăng nhập xem chi tiết một lịch hẹn thuộc về chính mình hoặc thành viên gia đình đã xác thực liên kết, kèm theo trạng thái hiệu lực và danh sách các hành động hợp lệ có thể thực hiện trên lịch hẹn đó.

Frontend (#53) đã được merge trước và cố định hợp đồng trong `medflow-ai-frontend/src/features/appointment/api/appointment-detail.api.ts` cùng `types/appointment-list.types.ts`:
- Gọi `GET /api/v1/appointments/{idOrCode}` trong đó màn danh sách truyền `appointmentCode` (`APT-yyyyMM-NNNN`) còn màn xác nhận đặt lịch truyền `id` số nguyên.
- Kiểu phản hồi là đối tượng JSON phẳng gồm toàn bộ trường của `AppointmentListItem`, bổ sung các trường thông tin định danh/liên lạc của bệnh nhân (`patientBirthYearLabel`, `patientBirthDateLabel`, `patientPhone`, `patientAddress`) và danh sách hành động khả dụng (`availableActions: string[]`).
- Frontend chỉ fallback sang dữ liệu mock khi gặp `404` hoặc lỗi mạng.

## Decision
1. **Phân tích định danh đầu vào (`idOrCode`) và thứ tự mã trạng thái HTTP (`400` / `404` / `403`)**:
   - Trim chuỗi đầu vào; nếu bắt đầu bằng `#` thì cắt ký tự `#` và `TrimStart()` tiếp.
   - Nếu rỗng, chỉ chứa khoảng trắng, dài quá `40` ký tự (độ dài cột `appointment_code`), hoặc chỉ gồm chữ số ASCII nhưng không parse được sang `long > 0` → ném `ValidationException("Mã lịch hẹn không hợp lệ.")` (`400 Bad Request`).
   - Nếu toàn bộ ký tự là chữ số ASCII hợp lệ → truy vấn theo `Appointment.Id == id`; ngược lại chuẩn hóa chữ hoa (`ToUpperInvariant()`) và truy vấn theo `Appointment.AppointmentCode.ToUpper() == code`.
   - Không tìm thấy bản ghi lịch hẹn hoặc thiếu dữ liệu slot bắt buộc (`Slot`, `StartsAt`, `EndsAt` là `null`) → ném `NotFoundException` (`404 Not Found`).
   - Tìm thấy lịch hẹn nhưng `PatientId` không nằm trong tập `PatientAccessScope` (bản thân hoặc thành viên gia đình `VERIFIED`, chưa thu hồi, còn hiệu lực) → ném `ForbiddenException("Bạn không có quyền xem chi tiết lịch hẹn này.")` (`403 Forbidden`), thống nhất với quy ước của endpoint danh sách lịch hẹn.
2. **Hợp đồng dữ liệu trả về (`AppointmentDetailDto`) và bảo vệ PHI**:
   - Sử dụng record phẳng `AppointmentDetailDto` giữ nguyên tên và thứ tự 42 trường của `AppointmentListItemDto`, nối tiếp bởi 4 trường PHI (`PatientBirthYearLabel`, `PatientBirthDateLabel`, `PatientPhone`, `PatientAddress`) và `AvailableActions`.
   - Không trả về `patientNationalId` vì cơ sở dữ liệu chỉ lưu `identification_hash` một chiều, không lưu số CCCD/CMND dạng rõ.
   - Các trường `clinicalSummary`, `cancellationReason`, `slotReleaseNote`, `rebookSpecialtyLabel` giữ giá trị `null` giống endpoint danh sách cho tới khi các use case tương ứng được triển khai.
3. **Quy tắc tính số thứ tự khám (`queueNumber`)**:
   - Nếu lịch hẹn đã có bản ghi `queue_entries` loại `EXAMINATION` thì ưu tiên hiển thị `QueueNumber` từ bảng `queue_entries` (định dạng 3 chữ số `D3`).
   - Nếu chưa có `queue_entries` và trạng thái hiệu lực thuộc nhóm chờ khám (`BOOKED`, `CONFIRMED`, `PENDING`), tính toán thứ hạng tại thời điểm đọc qua `AppointmentBookingRules.ComputeQueueNumberAsync` (theo quy tắc ở ADR 0003) để đồng nhất số thứ tự hiển thị với màn xác nhận đặt lịch.
4. **Quy tắc xác định hành động hợp lệ (`availableActions`) theo trạng thái, thời gian và chính sách bệnh viện**:
   - Thứ tự cố định trên wire: `CANCEL`, `RESCHEDULE`, `UPDATE_REASON`, `PRINT_SLIP`, `TRACK_QUEUE`, `REBOOK`.
   - Nhóm chỉnh sửa (`modifiable`): chỉ xét khi trạng thái hiệu lực là `BOOKED | CONFIRMED | PENDING` và `Slot.StartsAt > nowLocal` (giờ Việt Nam UTC+7). Khi và chỉ khi `modifiable == true`, nạp chính sách hiệu lực của bệnh viện (`IAppointmentPolicyService.GetEffectiveAsync` với `hospitalId` lấy từ `Slot.Service.HospitalId ?? Slot.Doctor.HospitalId ?? 0`):
     - `CANCEL`: `StartsAt >= nowLocal.AddHours(policy.CancelMinimumHours)`.
     - `RESCHEDULE`: `StartsAt >= nowLocal.AddHours(policy.RescheduleMinimumHours)`. Không kiểm tra `MaxReschedules` tại đây vì schema hiện chưa có bộ đếm số lần đổi lịch và quy tắc đó thuộc phạm vi use case đổi lịch (#56).
     - `UPDATE_REASON`: luôn bật khi `modifiable == true`.
   - `PRINT_SLIP`: bật khi trạng thái không thuộc nhóm đã hủy (`!IsCancelledStatus(status)`).
   - `TRACK_QUEUE`: bật khi trạng thái thuộc nhóm sắp tới (`CONFIRMED`, `BOOKED`, `PENDING`, `CHECKED_IN`).
   - `REBOOK`: bật khi trạng thái là `COMPLETED`, `CANCELLED` hoặc `NO_SHOW`.
5. **Tách `AppointmentReadModel` dùng chung và chính sách Audit Log**:
   - Trích xuất đồ thị `.Include/.ThenInclude` (`IncludeDisplayGraph`), truy vấn nạp dữ liệu liên quan (`LoadRelatedAsync` cho `visits`, `queue_entries`, `payments`), bộ xây dựng phạm vi thành viên (`BuildScopeMembers`) và ánh xạ `MapItem` sang lớp tĩnh nội bộ `AppointmentReadModel` để dùng chung giữa `AppointmentListService` và `AppointmentDetailService`, tránh nhân đôi logic hiển thị.
   - Không ghi `audit_logs` cho thao tác đọc chi tiết lịch hẹn này, đồng nhất với toàn bộ các endpoint `GET` khác trong hệ thống.

## Consequences
- Duoc:
  - Màn chi tiết lịch hẹn trên Frontend hoạt động trọn vẹn với cả liên kết từ danh sách (theo `appointmentCode`) lẫn từ màn đặt lịch thành công (theo `id`), hiển thị đúng thông tin PHI của hồ sơ trong phạm vi gia đình và các nút hành động hợp lệ theo cấu hình chính sách bệnh viện.
  - Logic ánh xạ và tính trạng thái hiệu lực giữa danh sách và chi tiết lịch hẹn được hợp nhất tại `AppointmentReadModel`, đảm bảo không lệch số liệu giữa hai màn hình.
- Mat:
  - Với các lịch hẹn sắp tới chưa có bản ghi `queue_entries`, endpoint chi tiết thực hiện thêm một truy vấn `COUNT` nhẹ qua `ComputeQueueNumberAsync` và một truy vấn đọc `HospitalPolicy` để tính `availableActions`.
- Rui ro con lai:
  - Khi triển khai use case đổi lịch (#56) và hủy lịch (#58), cần tái sử dụng cùng điều kiện thời gian tối thiểu theo `CancelMinimumHours` / `RescheduleMinimumHours` để đảm bảo nút hiển thị trên màn chi tiết khớp với kiểm tra thực thi khi submit.
