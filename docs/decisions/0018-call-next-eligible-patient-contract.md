# 0018 — Call Next Eligible Patient Contract & Queue Fairness Rotation (`#94` / UC31)

- Ngay: 2026-11-07
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#94` (`[M2][BE] Call Next Eligible Patient`, UC31) yêu cầu API chọn bệnh nhân đủ điều kiện tiếp theo tại một điểm phục vụ (`queue_service_points`) và chuyển lượt đó sang `CALLED`.

PR `#702` (`#92`, ADR 0017) đã hoàn thiện eligibility gate (`NOT_ARRIVED`, `LEFT`, `BLOCKED`, `AWAITING_RESULTS`), bảo vệ lịch hẹn đến hạn ở tier 0, và fast-track có gap gate ở tier 1. Phần còn thiếu cho `#94` là:
1. Áp dụng chính sách tỷ lệ `walkin_priority_ratio` và bảng trạng thái `queue_fairness_states` để luân phiên công bằng giữa nhóm ưu tiên (`ELDERLY`, `PREGNANT`, `RETURN_CONSULTATION`) và nhóm thường (`NORMAL`).
2. Đồng nhất thứ tự `servingOrder` hiển thị trên bảng hàng đợi với thứ tự gọi thực tế.
3. Đảm bảo giao dịch gọi số an toàn khi có nhiều nhân viên (Lễ tân / Bác sĩ) thao tác đồng thời.

## Decision

1. **Endpoint & phân quyền 2 lớp**:
   - `POST /api/v1/queue/service-points/{servicePointId}/call-next` (body tùy chọn `{"notes": "..."}`).
   - Lớp 1 (`[Authorize]`): cho phép `DOCTOR,RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN` để phục vụ cả màn hình Bác sĩ (`/doctor`) và Lễ tân (`/reception/queue`).
   - Lớp 2 (Ownership): `RECEPTION`/`HOSPITAL_ADMIN`/`SYSTEM_ADMIN` chỉ được gọi tại các điểm phục vụ thuộc bệnh viện của mình (kèm kiểm tra quyền quản lý hàng đợi); `DOCTOR` chỉ được gọi tại điểm phục vụ có `QueueServicePoint.DoctorId == actorDoctor.Id`.
2. **Quy ước bộ đếm `queue_fairness_states.consecutive_priority_started` (`QueueFairnessRotation`)**:
   - `counter >= 0`: số lượt thuộc nhóm ưu tiên (`ELDERLY`/`PREGNANT`/`RETURN_CONSULTATION`) đã gọi liên tiếp.
   - `counter < 0`: `-counter` là số lượt `NORMAL` đã gọi liên tiếp.
   - Giá trị luôn nằm trong đoạn `[-NormalWeight, PriorityWeight]` theo `walkin_priority_ratio` (mặc định `3:1`).
   - Khi `policy_version` của `walkin_priority_ratio` thay đổi so với `queue_fairness_states.policy_version`, bộ đếm tự động reset về `0`.
   - `ExaminationQueueOrderingEngine.Evaluate` nhận `consecutivePriorityCalls` để trộn (interleave) tier 1 (nhóm ưu tiên đủ điều kiện fast-track) và tiers 2–4 (nhóm đủ điều kiện còn lại) ngay trước khi gán `servingOrder`. Nếu một bên rỗng thì lấy bên còn lại để không nhóm nào bị bỏ đói.
3. **Cập nhật trạng thái công bằng lúc `CALLED`**:
   - Bộ đếm `consecutive_priority_started` và `last_started_queue_entry_id` được cập nhật ngay khi gọi bệnh nhân (`CALLED`), giúp lần đọc hàng đợi hoặc lần gọi kế tiếp phản ánh đúng lượt vừa gọi mà không cần sửa `QueueStatusService` lúc chuyển `IN_PROGRESS`.
   - Lượt gọi do lịch hẹn đến hạn (`IsDueProtected == true`, `SelectionReason = "APPOINTMENT_DUE"`) **không** làm thay đổi bộ đếm công bằng giữa walk-in ưu tiên và walk-in thường.
   - Với lượt `RETURN_CONSULTATION`, chỉ cập nhật `queue_entries.status = "CALLED"`, giữ nguyên `visits.status` để không làm mất trạng thái lâm sàng hiện tại của đợt khám.
4. **Phân biệt kết quả nghiệp vụ (200) và xung đột trạng thái (409)**:
   - Trả `200 OK` kèm `Outcome = "CALLED" | "NO_ELIGIBLE_PATIENT" | "HOLD_FOR_SLOT"` khi điểm phục vụ hợp lệ và rảnh. Khi `NO_ELIGIBLE_PATIENT` hoặc `HOLD_FOR_SLOT`, không ghi DB và không sinh audit log.
   - Trả `409 Conflict` khi: điểm phục vụ đang ngừng hoạt động; phòng đang có bệnh nhân `CALLED` hoặc `IN_PROGRESS`; phòng khám chỉ là bucket fallback chưa cấu hình `queue_service_points` (`sp-room-<id>`); hoặc dữ liệu bị thay đổi đồng thời (`state_version` mismatch, concurrency token trên `queue_fairness_states`, hoặc trùng khóa chính khi chèn dòng fairness đầu ngày).

## Consequences

- **Duoc**:
  - Một nguồn sự thật duy nhất (`ExaminationQueueOrderingEngine` + `QueueFairnessRotation`) cho cả hiển thị `servingOrder` (UC28/UC30) và lệnh `call-next` (UC31).
  - Chặn gọi trùng khi hai nhân viên bấm gọi cùng lúc nhờ kiểm tra lại entity tracked trong transaction kết hợp concurrency token trên `queue_fairness_states`.
  - Lưu đầy đủ vết kiểm toán `QUEUE_CALL_NEXT` kèm `selectionReason` và trạng thái fairness trước/sau.
- **Mat**:
  - `BuildQueueBoardForHospitalAsync` cần thêm 1 truy vấn đọc `queue_fairness_states` theo ngày và danh sách điểm phục vụ của bệnh viện.
- **Rui ro con lai**:
  - Nếu bệnh nhân được gọi (`CALLED`) nhưng sau đó bị đánh dấu `SKIPPED` (qua lượt), lượt gọi đó vẫn đã được tính vào chu kỳ rotation. Đây là hành vi chấp nhận được để tránh một nhóm ưu tiên vắng mặt liên tục làm chặn hoàn toàn nhóm còn lại.
