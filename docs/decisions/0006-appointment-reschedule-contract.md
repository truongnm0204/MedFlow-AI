# 0006 — Hợp đồng API và quy tắc đổi lịch hẹn (`POST /api/v1/appointments/{idOrCode}/reschedule`)

- Ngay: 2026-11-02
- Trang thai: accepted
- Lane: high-risk

## Context

Use case UC13 (Reschedule Appointment — M2-004 / #56) cho phép bệnh nhân (`PATIENT`) đổi một lịch hẹn của chính mình hoặc của thành viên gia đình đã xác thực (`VERIFIED`) sang một khung giờ khác còn khả dụng, theo đúng chính sách đặt lịch của bệnh viện.

Frontend đã cố định hợp đồng gọi API tại `medflow-ai-frontend/src/features/appointment/api/appointment-detail.api.ts` → `rescheduleAppointmentApi`:
- `POST /api/v1/appointments/{idOrCode}/reschedule`, body `{ "targetSlotId": string, "rescheduleNote"?: string }` (zod của FE giới hạn `rescheduleNote` tối đa 250 ký tự).
- Response `200 OK`: **lịch hẹn mới** dưới dạng `AppointmentDetailDto`, là superset của `AppointmentListItem` phía FE.

## Decision

1. **Thứ tự kiểm tra và mã trạng thái HTTP** (ánh xạ qua `ExceptionMiddleware`: `ValidationException`→400, `NotFoundException`→404, `UnauthorizedAccessException`→401, `ForbiddenException`→403, `ConflictException`→409):
   - `userId <= 0` → 401 `"Thông tin định danh người dùng không hợp lệ."`; `request == null` → `ArgumentNullException`.
   - `idOrCode` parse qua `AppointmentBookingRules.ParseAppointmentIdentifier` (trim, cắt `#`, tối đa 40 ký tự, số dương); sai → 400 `"Mã lịch hẹn không hợp lệ."`.
   - `TargetSlotId` rỗng / không parse được `long > 0` → 400 `"Vui lòng chọn khung giờ mới hợp lệ để đổi lịch."`.
   - `RescheduleNote` sau trim > 250 ký tự → 400 `"Ghi chú đổi lịch tối đa 250 ký tự."` (rỗng sau trim lưu `null`).
   - Không tìm thấy lịch hẹn hoặc thiếu `Slot`/`StartsAt`/`EndsAt` → 404 `NotFoundException("Appointment", trimmed)`.
   - `PatientId` không thuộc `PatientAccessScope` (bản thân hoặc thành viên `VERIFIED` còn hiệu lực qua `LoadOwnedPatientsAsync`) → 403 `"Bạn không có quyền đổi lịch hẹn này."` — kiểm tra **sau** 404, giống ADR 0005.
   - Trạng thái hiệu lực (`AppointmentReadModel.ResolveEffectiveStatus` + `Visit` mới nhất) không thuộc `BOOKED|CONFIRMED|PENDING`, hoặc `Slot.StartsAt <= nowLocal` → 409 `"Lịch hẹn ở trạng thái hiện tại không thể đổi lịch."` — khớp điều kiện `modifiable` trong `BuildAvailableActionsAsync`.
   - `Slot.StartsAt < nowLocal.AddHours(policy.RescheduleMinimumHours)` → 400 `"Chỉ được đổi lịch trước giờ khám tối thiểu {N} giờ."` (biên `>=` được phép, giống rule `RESCHEDULE` của ADR 0005). `hospitalId` lấy `Slot.Service.HospitalId ?? Slot.Doctor.HospitalId ?? 0`.
   - `Slot.Service.AllowsOnlineBooking != true` → 400 `"Dịch vụ khám này không hỗ trợ đặt lịch trực tuyến."`.
   - `targetSlotId == current.Id` → 400 `"Khung giờ mới trùng với khung giờ hiện tại của lịch hẹn."`.
   - Không tìm thấy slot đích → 404 `NotFoundException("ExaminationSlot", targetSlotId)`.
   - `target.ServiceId != current.ServiceId` hoặc (current có `DoctorId` mà `target.DoctorId != current.DoctorId`) → 400 `"Khung giờ mới phải cùng dịch vụ và bác sĩ với lịch hẹn hiện tại."`. Nếu slot hiện tại không có bác sĩ thì chỉ cần cùng dịch vụ.
   - `EnsureSlotBookable(target, ...)` trước khi vào giao dịch để trả lỗi sớm như pre-check của booking.

2. **Hủy + tạo mới (Cancel & Recreate), không cập nhật tại chỗ.** Bản ghi cũ nhận `Status = "CANCELLED"`; một bản ghi `appointments` mới được tạo với `Id` mới và mã `APT-{yyyyMM}-{Id:D4}`. Lý do: `QueueNumber` được tính lúc đọc từ `Id <= appointment.Id` trong cùng ngày và nhóm bác sĩ/phòng (ADR 0003 §8). Di chuyển bản ghi tại chỗ sẽ dồn số thứ tự của bệnh nhân khác, vi phạm nguyên tắc "queue number không tự đổi". `ResolveEffectiveStatus` ánh xạ trạng thái lạ về `PENDING`, nên bản ghi cũ bắt buộc dùng `CANCELLED`, không đặt trạng thái mới.

3. **`MaxReschedules` không được enforce.** Schema hiện không có cột đếm số lần đổi lịch; cần migration tương lai. Đây là quyết định có ý thức, được ghi lại.

4. **`rescheduleNote` chỉ lưu trong `audit_logs.change_data`.** `reason_for_visit` được copy nguyên vẹn sang lịch hẹn mới. Một entry audit duy nhất `APPOINTMENT_RESCHEDULE` trên `EntityId = <id lịch cũ>`, `change_data` chứa `FromAppointmentId/FromAppointmentCode/FromSlotId/ToAppointmentId/ToAppointmentCode/ToSlotId/PatientId/RescheduleNote`.

5. **Tái dùng lock order của booking.** Trong transaction: `IAppointmentBookingLock.AcquireAsync(patientId, target.DoctorId, target.Id)` theo thứ tự cố định patient → doctor → slot, nên không deadlock với booking. Sau khi khóa, re-validate: trạng thái hiệu lực của dòng được track (phát hiện cancel/reschedule đồng thời), `EnsureSlotBookable`, `EnsureSlotHasCapacityAsync`, `EnsurePatientLimitsAsync(..., excludeAppointmentId = tracked.Id)`. Lịch cũ được loại khỏi kiểm tra overlap và `MaxActiveBookings`, nên đổi sang slot trùng giờ với slot cũ hoặc khi đã đạt giới hạn vẫn thành công.

6. **Trả về `AppointmentDetailDto` qua `IAppointmentDetailService.GetMyAppointmentDetailAsync`** với id mới — tái dùng mapping, `QueueNumber` (`ComputeQueueNumberAsync`, id mới đứng cuối nhóm) và `availableActions` giống GET detail.

7. **`queue_entries` trên lịch cũ để nguyên.** `QueueEntry` không có cột status; #58 Cancel cũng để gap tương tự.

## Consequences

- Duoc:
  - Đảm bảo queue number của các bệnh nhân khác không bị dồn khi một lịch được đổi giờ.
  - Tái dùng toàn bộ rule booking (bookable, capacity, patient limits, lock order) qua `AppointmentBookingRules`, không nhân đôi logic.
  - Response `200` và các lỗi `400/401/403/404/409` khớp hợp đồng FE.
- Mat:
  - Hai lần `SaveChangesAsync` trong cùng transaction (sinh `Id` cho mã `APT-{yyyyMM}-{Id:D4}`).
  - `EnsurePatientLimitsAsync` nhận thêm tham số `excludeAppointmentId` — thay đổi chữ ký của helper dùng chung.
- Rui ro con lai:
  - FE phải chuyển sang dùng `id`/`appointmentCode` **mới** từ response; mọi tham chiếu tới id cũ (đã `CANCELLED`) không còn thao tác được.
  - `queue_entries` mồ côi trên lịch cũ vẫn tồn tại cho tới khi #58 xử lý.
  - `MaxReschedules` chưa enforce → có thể lạm dụng đổi lịch không giới hạn cho tới khi bổ sung cột đếm.
