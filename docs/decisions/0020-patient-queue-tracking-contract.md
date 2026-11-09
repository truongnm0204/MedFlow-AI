# 0020 — Hợp đồng API bệnh nhân theo dõi hàng đợi khám (`GET /api/v1/appointments/{idOrCode}/queue-status`)

- Ngay: 2026-11-08
- Trang thai: accepted
- Lane: high-risk

## Context

Use case **UC16 — Track Examination Queue** (`#86` / `M2-034`) cho phép bệnh nhân đã đăng nhập (`PATIENT`) theo dõi trực tiếp trên cổng bệnh nhân:
1. Số thứ tự khám ngoại trú (`queueNumber`) và mã lượt khám (`visitCode`).
2. Điểm phục vụ lâm sàng thực tế (`roomName`, `doctorName`, `departmentName`), bao gồm cả trường hợp đã được điều phối hoặc chuyển buồng khám ở `#80` / `#82`.
3. Số thứ tự đang khám / vừa gọi (`servingQueueNumber`), số thứ tự đủ điều kiện kế tiếp (`nextQueueNumber`) và thời gian chờ ước tính (`estimatedWaitLabel`) tại buồng khám.
4. Tiến trình khám 3 bước (`visitSteps`: `CHECK_IN`, `CONSULTATION`, `COMPLETE`) và hướng dẫn hành động (`guidanceNote`).

Frontend cổng bệnh nhân (`#85`, `medflow-ai-frontend/src/features/appointment/api/appointment-queue.api.ts`) đã gọi `GET /api/v1/appointments/{idOrCode}/queue-status` theo hợp đồng `AppointmentQueueStatus` trong `appointment-queue.types.ts`. Toàn bộ dữ liệu cần thiết đã có trong PostgreSQL schema v4.1 (`appointments`, `visits`, `queue_entries`, `queue_service_points`, `queue_fairness_states`, `hospital_policies`), không phát sinh migration mới.

## Decision

1. **Hợp đồng HTTP & Phân quyền 2 lớp (`GET /api/v1/appointments/{idOrCode}/queue-status`)**:
   - **Lớp 1 (`[Authorize(Roles = "PATIENT")]`)**: đặt trên `AppointmentsController`. Nhân viên nội bộ (`DOCTOR`, `RECEPTION`, `HOSPITAL_ADMIN`, `SYSTEM_ADMIN`) dùng các API hàng đợi nội bộ (`/api/v1/queue/*`).
   - **Lớp 2 (Ownership)**: tái sử dụng `AppointmentReadModel.LoadOwnedAppointmentViewAsync` để kiểm tra `Appointment.PatientId` thuộc phạm vi `PatientAccessScope` (bản thân người dùng đang đăng nhập hoặc thành viên gia đình có liên kết `VERIFIED` chưa bị thu hồi).
   - Giữ nguyên thứ tự ưu tiên mã lỗi với `GET /api/v1/appointments/{idOrCode}` và `status-tracking`:
     - `401 Unauthorized`: `userId <= 0` hoặc tài khoản/hồ sơ bệnh nhân không hợp lệ.
     - `400 Bad Request`: `idOrCode` rỗng, số `<= 0`, dài `> 40` ký tự hoặc sai định dạng.
     - `404 Not Found`: không tìm thấy lịch hẹn theo `Id` hoặc `AppointmentCode` (chấp nhận tiền tố `#`, không phân biệt hoa thường), hoặc lịch hẹn thiếu `Slot`/`StartsAt`/`EndsAt`.
     - `403 Forbidden`: lịch hẹn của bệnh nhân khác hoặc người thân đã thu hồi liên kết → `"Bạn không có quyền xem hàng đợi của lịch hẹn này."`.
2. **Xử lý trạng thái hủy / vắng mặt (`409 Conflict`)**:
   - Khi lịch hẹn hoặc lượt khám ở trạng thái `CANCELLED` → trả `409 Conflict` với thông điệp `"Lịch hẹn đã hủy nên không còn số thứ tự trong hàng đợi."`.
   - Khi lịch hẹn hoặc lượt khám ở trạng thái `NO_SHOW` → trả `409 Conflict` với thông điệp `"Lịch hẹn đã ghi nhận vắng mặt nên không còn số thứ tự trong hàng đợi."`.
   - Khi `QueueEntry` lâm sàng hiệu lực bị `CANCELLED` → trả `409 Conflict` với thông điệp `"Số thứ tự trong hàng đợi của lượt khám này đã bị hủy."`.
   - Việc trả `409 Conflict` (thay vì `404`) đảm bảo frontend không kích hoạt fallback dữ liệu mock (`shouldFallbackToMock` chỉ bắt `404` và lỗi mạng) và hiển thị đúng cảnh báo nghiệp vụ.
3. **Đồng bộ buồng khám / bác sĩ và đánh giá bucket hàng đợi qua `ExaminationQueueOrderingEngine`**:
   - Nếu `Visit.ExaminationRoomId` hoặc `Visit.AssignedDoctorId` khác với `Slot`, ưu tiên hiển thị tên phòng, khoa và bác sĩ theo `Visit` (phản ánh kết quả gán/chuyển phòng từ `#80` / `#82`).
   - Nạp các `QueueEntry` lâm sàng (`EXAMINATION`, `RETURN_CONSULTATION`) cùng ngày tại điểm phục vụ (`targetServicePointId`, hoặc phòng/bác sĩ tương ứng), nạp chính sách hàng đợi (`QueueOrderingPolicyLoader`) và bộ đếm luân phiên công bằng (`QueueFairnessStates` + `QueueFairnessRotation`), sau đó chạy `ExaminationQueueOrderingEngine.Evaluate`.
   - `servingQueueNumber` lấy từ lượt `IN_PROGRESS` (ưu tiên) hoặc `CALLED` đầu tiên trong bucket; `nextQueueNumber` lấy từ lượt đầu tiên có `Eval.IsEligible == true` (hoặc lượt `READY`/`WAITING` chưa rời phòng chờ).
   - `estimatedWaitLabel` khi đang chờ khám tiêu chuẩn tính bằng `Math.Max(1, aheadCount) * (policy.ExpectedDurationMinutes ?? 15)` phút (hoặc `"Chuẩn bị đến lượt khám"` khi không có ai phía trước và phòng đang trống).
4. **Ánh xạ trạng thái `status` và tiến trình 3 bước (`visitSteps`)**:
   - Giữ `status` trong tập giá trị frontend `{"AWAITING_CHECK_IN", "WAITING", "COMPLETED"}`:
     - `AWAITING_CHECK_IN` (`CHỜ CHECK-IN`): lịch hẹn `BOOKED`/`CONFIRMED`/`PENDING` chưa check-in → bước 1 `current` (`CHƯA CHECK-IN`), bước 2 và 3 `upcoming`.
     - `WAITING`: đã check-in hoặc đang trong vòng đời hàng đợi khám → bước 1 `done` (`Đã check-in`), bước 2 `current` phản ánh chi tiết các trạng thái con (`CHƯA BẮT ĐẦU`, `ĐÃ GỌI SỐ`, `ĐANG KHÁM`, `TẠM QUA LƯỢT`, `CHỜ KẾT QUẢ CLS`), bước 3 `upcoming`.
     - `COMPLETED` (`ĐÃ HOÀN THÀNH`): lượt khám đã hoàn tất → cả 3 bước `done`, `estimatedWaitLabel = null`.

## Consequences

- **Duoc**:
  - Đồng nhất thứ tự gọi số và thời gian chờ ước tính trên cổng bệnh nhân (UC16) với màn hình điều phối hàng đợi của Lễ tân/Bác sĩ (UC28/UC30/UC31) nhờ dùng chung `ExaminationQueueOrderingEngine`.
  - Chỉ đánh giá duy nhất bucket điểm phục vụ của lịch hẹn thay vì dựng toàn bộ bảng hàng đợi bệnh viện.
- **Mat**:
  - Cần truy vấn các `QueueEntry` và `Visit` cùng ngày của điểm phục vụ tương ứng trong mỗi lần gọi endpoint theo dõi hàng đợi.
- **Rui ro con lai**:
  - Thời gian chờ ước tính dựa trên `expected_duration_minutes` trung bình của chính sách hàng đợi, mang tính tham khảo cho người bệnh.
