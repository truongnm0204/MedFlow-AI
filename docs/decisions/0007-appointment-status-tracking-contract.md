# 0007 — Hợp đồng API theo dõi trạng thái lịch hẹn (`GET /api/v1/appointments/{idOrCode}/status-tracking`)

- Ngay: 2026-11-01
- Trang thai: accepted
- Lane: high-risk

## Context
Use case UC15 (M2-008 / #60) cho phép bệnh nhân đã đăng nhập theo dõi **trạng thái hiện tại** của một lịch hẹn của bản thân hoặc người thân đã xác thực liên kết (`VERIFIED`), kèm theo **timeline các mốc** và **bước tiếp theo** gợi ý. M1 đã có danh sách (`GET /api/v1/appointments/me`, ADR 0004) và chi tiết (`GET /api/v1/appointments/{idOrCode}`, ADR 0005) nhưng chưa có một hợp đồng riêng cho timeline trạng thái. Frontend (#59) sẽ tiêu thụ hợp đồng này ở một task riêng. Mọi mốc thời gian cần thiết (`visits.checked_in_at`, `visits.completed_at`, `queue_entries.called_at`, `queue_entries.started_at`) đã tồn tại trong schema v4.1 — chỉ chưa được ánh xạ trong EF — nên **không tạo migration mới**.

## Decision
1. **Hợp đồng HTTP (`GET /api/v1/appointments/{idOrCode}/status-tracking`)**:
   - Yêu cầu xác thực JWT với role `PATIENT` (layer 1 authz `[Authorize(Roles = "PATIENT")]`).
   - Tham số đường dẫn `idOrCode` chấp nhận `Id` số nguyên dương hoặc `AppointmentCode` (có thể kèm tiền tố `#`), dùng chung `AppointmentReadModel.ParseIdentifier` với endpoint chi tiết.
   - Response `200 OK`: `AppointmentStatusTrackingDto { appointmentId, appointmentCode, patientName, patientRelationshipLabel, status, statusLabel, scheduledDateFullLabel, scheduledTimeRange, doctorName, departmentName, roomName, queueNumber, steps[], nextStep }`.
   - `steps[]`: `AppointmentStatusStepDto { key, title, state, occurredAt }` với `state ∈ {"done","current","upcoming"}` (lowercase, khớp FE `VisitStepState`) và `occurredAt` là ISO `yyyy-MM-ddTHH:mm:ss+07:00` hoặc `null`.
   - `nextStep`: `AppointmentNextStepDto { code, title, description, dueAt }` với `dueAt` ISO `+07:00` hoặc `null`.
   - Không trả PHI (phone/address/DOB) và không trả `availableActions` (đã có ở endpoint chi tiết).
   - Các trường hiển thị lấy từ `AppointmentListItemDto` do `AppointmentReadModel.MapItem` trả về để đồng nhất với list/detail; `statusLabel = AppointmentReadModel.ResolveStatusBannerLabel(status)`.
2. **Thứ tự kiểm tra và mã trạng thái lỗi (`ProblemDetails`)** — giữ nguyên thứ tự của endpoint chi tiết (dùng chung `AppointmentReadModel.LoadOwnedAppointmentViewAsync`):
   - `401 Unauthorized` (`UnauthorizedAccessException`): `userId <= 0` hoặc token không hợp lệ → `"Thông tin định danh người dùng không hợp lệ."`.
   - `400 Bad Request` (`ValidationException`): `idOrCode` không hợp lệ → `"Mã lịch hẹn không hợp lệ."`.
   - `404 Not Found` (`NotFoundException`): không tìm thấy lịch hẹn theo `Id`/`AppointmentCode`.
   - `403 Forbidden` (`ForbiddenException`): `PatientId` của lịch hẹn không thuộc phạm vi sở hữu `PatientAccessScope` → `"Bạn không có quyền theo dõi trạng thái lịch hẹn này."` (layer 2 authz).
   - `404 Not Found` (`NotFoundException`): bản ghi lịch hẹn thiếu `Slot`, `StartsAt` hoặc `EndsAt`.
3. **Ánh xạ cột timestamp có sẵn (không migration)**:
   - `Visit` thêm `CheckedInAt`/`CompletedAt` (`DateTime?`) → cột `checked_in_at`/`completed_at`.
   - `QueueEntry` thêm `CalledAt`/`StartedAt` (`DateTime?`) → cột `called_at`/`started_at`.
   - Không ánh xạ `queue_entries.status`/`completed_at` (không dùng tới).
4. **Quy tắc timeline `steps`** (input: `status` = `item.Status` sau `ResolveEffectiveStatus`; `bookedAt`, `cancelledAt`, `checkedInAt`, `calledAt`, `startedAt`, `completedAt` lấy từ view):
   - `status == "CANCELLED"` → `[BOOKED "Đặt lịch thành công" done bookedAt, CANCELLED "Đã hủy lịch" done cancelledAt]`.
   - `status == "NO_SHOW"` → `[BOOKED done bookedAt, NO_SHOW "Vắng mặt" done null]`.
   - Còn lại → 4 mốc `BOOKED "Đặt lịch thành công"` (bookedAt), `CHECKED_IN "Check-in tại quầy tiếp đón"` (checkedInAt), `IN_CONSULTATION "Vào phòng khám"` (startedAt), `COMPLETED "Hoàn tất khám"` (completedAt). Mốc "đã đạt":
     - BOOKED: luôn đạt.
     - CHECKED_IN: `status is "CHECKED_IN" or "COMPLETED"`.
     - IN_CONSULTATION: `status == "COMPLETED" || (status == "CHECKED_IN" && startedAt != null)`.
     - COMPLETED: `status == "COMPLETED"`.
     - State: mốc đã đạt → `done`; mốc chưa đạt đầu tiên → `current`; các mốc sau → `upcoming`. Mốc chưa đạt luôn `occurredAt = null` (kể cả khi DB có timestamp lệch).
   - Bất biến: trạng thái kết thúc (`COMPLETED`/`CANCELLED`/`NO_SHOW`) → mọi step `done`, không có `current`; trạng thái chưa kết thúc → đúng một step `current`.
5. **Quy tắc `nextStep`** (xét theo thứ tự, lấy nhánh đầu tiên khớp; `roomLabel = RoomName` hoặc `"phòng khám"` nếu trống):

   |Điều kiện|Code|Title|Description|DueAt|
   |---|---|---|---|---|
   |`CANCELLED`|`REBOOK`|`Đặt lịch mới`|`Lịch hẹn đã được hủy. Bạn có thể đặt lịch khám mới khi cần.`|null|
   |`NO_SHOW`|`REBOOK`|`Đặt lịch mới`|`Bạn đã vắng mặt ở lịch hẹn này. Vui lòng đặt lịch khám mới nếu vẫn cần thăm khám.`|null|
   |`COMPLETED`|`FOLLOW_UP`|`Theo dõi sau khám`|`Lượt khám đã hoàn tất. Vui lòng làm theo chỉ định của bác sĩ và đặt lịch tái khám nếu được hẹn.`|null|
   |`PENDING`|`WAIT_FOR_CONFIRMATION`|`Chờ bệnh viện xác nhận`|`Lịch hẹn đang chờ bệnh viện xác nhận. Vui lòng theo dõi trạng thái trước ngày khám.`|null|
   |`BOOKED`/`CONFIRMED` và `start >= nowLocal`|`CHECK_IN`|`Check-in tại quầy tiếp đón`|`"Vui lòng có mặt và check-in trước {start-15m:HH:mm} ngày {start:dd/MM/yyyy}."`|`FormatVietnamTimestamp(start.AddMinutes(-15))`|
   |`BOOKED`/`CONFIRMED` và `start < nowLocal`|`CONTACT_RECEPTION`|`Liên hệ quầy tiếp đón`|`Đã quá giờ hẹn nhưng lịch hẹn chưa được check-in. Vui lòng liên hệ quầy tiếp đón để được hỗ trợ.`|null|
   |`CHECKED_IN` và `startedAt != null`|`IN_CONSULTATION`|`Đang khám`|`Bạn đang trong lượt khám. Vui lòng làm theo hướng dẫn của bác sĩ.`|null|
   |`CHECKED_IN` và `calledAt != null`|`GO_TO_ROOM`|`Vào phòng khám`|`"Đã đến lượt của bạn. Vui lòng vào {roomLabel}."`|null|
   |`CHECKED_IN` (còn lại)|`WAIT_FOR_CALL`|`Chờ gọi số`|có queueNumber: `"... đến khi được gọi số {queueNumber}."`; không có: `"... đến khi được gọi tên."`|null|

   - Ngưỡng `-15 phút` khớp `checkInDeadlineTime` hiện có trong `MapItem`. Không suy luận `NO_SHOW` theo thời gian (giữ ADR 0004 mục 2).
   - `ResolveEffectiveStatus` chỉ trả 7 giá trị trên → `switch` kèm `_ => throw new InvalidOperationException(...)` để bắt lỗi lập trình.
6. **Tái sử dụng và audit**:
   - Logic nạp + kiểm tra sở hữu dùng chung được gom vào `AppointmentReadModel.LoadOwnedAppointmentViewAsync` (refactor từ `AppointmentDetailService`, giữ nguyên behavior); số thứ tự hiển thị qua `ResolveDisplayQueueNumberAsync`; định dạng thời gian qua `FormatVietnamTimestamp`.
   - Không ghi `audit_logs` (đồng nhất ADR 0005 mục 5: GET read-only không audit). Không dùng `IAppointmentPolicyService`.

## Consequences
- Duoc:
  - Có hợp đồng timeline + next-step rõ ràng, tái sử dụng toàn bộ logic nạp/kiểm tra sở hữu của endpoint chi tiết → thứ tự lỗi và phạm vi truy cập nhất quán tuyệt đối.
  - Không phát sinh migration; chỉ ánh xạ thêm cột đã có trong schema v4.1.
- Mat:
  - `AppointmentReadModel` tăng trách nhiệm (thêm loader + 2 helper) — chấp nhận được vì là điểm dùng chung của read-model lịch hẹn.
- Rui ro con lai:
  - Timeline chỉ phản ánh các mốc có timestamp sẵn; lịch sử đổi lịch (audit trail) chưa đưa vào — nếu cần sẽ là quyết định riêng ở task sau.
