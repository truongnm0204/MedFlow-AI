# 0022 — Open Consultation Workspace Contract (`#106` / `M2-052` / UC31)

- Ngay: 2026-04-20
- Trang thai: accepted
- Lane: high-risk

## Context

Sau khi Bác sĩ xem hàng đợi được phân công (`GET /api/v1/doctor/assigned-queue` — UC30) hoặc gọi bệnh nhân đủ điều kiện tiếp theo vào buồng khám (`POST /api/v1/queue/service-points/{servicePointId}/call-next`), Bác sĩ cần mở không gian khám lâm sàng (Consultation Workspace — UC31) cho ca khám hiện tại để:
1. Chuyển trạng thái lượt xếp hàng và lượt khám từ `CALLED` sang `IN_PROGRESS` một cách nhất quán, chống xung đột đồng thời (`state_version`) và đảm bảo mỗi buồng khám/điểm phục vụ chỉ có tối đa một ca `IN_PROGRESS` tại một thời điểm.
2. Tự động khởi tạo hồ sơ bệnh án nháp (`medical_records.status = 'DRAFT'` + `medical_record_revisions.revision_no = 1` lấy `presenting_complaint` từ lý do khám) nếu ca khám chưa có hồ sơ bệnh án.
3. Xem đầy đủ thông tin hành chính của bệnh nhân (ẩn `identification_hash`), bối cảnh hàng đợi (`ExaminationQueueOrderingEngine`), sinh hiệu (`vital_signs` JSONB), chẩn đoán (`visit_diagnoses`), tóm tắt cận lâm sàng (`DIAGNOSTIC` queue readiness), đơn thuốc (`prescriptions`) và danh sách thao tác lâm sàng được phép (`allowedActions` gắn với UC31–UC38 và quyền RBAC).

## Decision

1. **Tách biệt rõ hành vi Xem (`GET`) và Mở phiên khám (`POST`)**:
   - `GET /api/v1/doctor/consultation/{idOrCode}` (cùng các alias `/api/v1/doctor/visits/{idOrCode}/consultation` và `/api/v1/visits/{idOrCode}/consultation`): chỉ đọc dữ liệu (`actionOutcome = "VIEWED"`), không thay đổi trạng thái DB; cho phép Bác sĩ xem cả ca khám đang chờ, đang khám hoặc đã hoàn tất (`COMPLETED`).
   - `POST /api/v1/doctor/consultation/{idOrCode}/open` (cùng các alias `/api/v1/doctor/visits/{idOrCode}/open-consultation` và `/api/v1/visits/{idOrCode}/open-consultation`):
     - Nếu lượt khám đang ở `CALLED`: kiểm tra `ExpectedStateVersion` (nếu truyền), kiểm tra bệnh nhân `PRESENT`, phòng khám/điểm phục vụ `ACTIVE`, điều kiện kết quả cận lâm sàng bắt buộc (`final_result_required` cho `RETURN_CONSULTATION`), và không có bệnh nhân khác đang `IN_PROGRESS` tại cùng buồng khám/điểm phục vụ; sau đó chuyển `QueueEntry.Status` và `Visit.Status` sang `IN_PROGRESS`, tăng `StateVersion`, khởi tạo `MedicalRecord` (`DRAFT`) + `MedicalRecordRevision` (`revision_no = 1`), ghi `AuditLog` (`CONSULTATION_WORKSPACE_OPENED`), trả về `actionOutcome = "OPENED"`.
     - Nếu lượt khám đã ở `IN_PROGRESS`: xử lý idempotent (`actionOutcome = "ALREADY_IN_PROGRESS"`), đảm bảo `MedicalRecord` nháp tồn tại nhưng không tăng `StateVersion` và không ghi trùng `AuditLog`.
     - Nếu lượt khám ở `WAITING`, `READY`, `SKIPPED`, `COMPLETED`, `CANCELLED`, `NO_SHOW`: trả về `409 Conflict`.
2. **Bảo mật & Phân quyền 2 lớp (`DOCTOR` + Ownership)**:
   - Layer 1: `[Authorize(Roles = "DOCTOR")]` tại `DoctorConsultationController`.
   - Layer 2 tại `ConsultationWorkspaceService`: xác thực trạng thái `User` (`ACTIVE`, không bị khóa), role `DOCTOR` trong DB, `Staff` (`ACTIVE`, có `HospitalId`), `Doctor` (`IsActive`, khớp `HospitalId`), `Hospital` (`IsActive`), quyền `MEDICAL_RECORD_WRITE` khi gọi `POST .../open`, và kiểm tra quyền sở hữu ca khám/buồng khám (`visit.AssignedDoctorId == actorDoctor.Id || servicePoint.DoctorId == actorDoctor.Id` và cùng `HospitalId`).
3. **Ánh xạ bảng hiện hữu (`medical_records`, `medical_record_revisions`, `visit_diagnoses`)**:
   - Ánh xạ trực tiếp các bảng đã có trong `database/medflow_schema_v4_1.sql` mà không tạo migration SQL mới; khai báo default interface properties trên `IApplicationDbContext` để giữ tương thích ngược với toàn bộ test doubles hiện có.

## Consequences

- **Duoc**:
  - Đảm bảo tính toàn vẹn của máy trạng thái hàng đợi (`CALLED -> IN_PROGRESS`) và kiểm soát đồng thời qua `state_version`.
  - Cung cấp aggregate đầy đủ cho giao diện Consultation Workspace của Bác sĩ mà không làm lộ dữ liệu nhạy cảm (`identification_hash`).
  - Giữ tương thích 100% với các unit test doubles hiện hữu nhờ default interface methods trên `IApplicationDbContext`.
- **Mat**:
  - Cần 2 bước rõ ràng khi bệnh nhân đang ở `WAITING`/`READY` (gọi số `call-next` sang `CALLED` trước, sau đó `open` sang `IN_PROGRESS`).
- **Rui ro con lai**:
  - Các tính năng ghi nhận khám lâm sàng chi tiết (UC33), chỉ định CLS (UC34), chẩn đoán (UC36), kê đơn (UC37) và hoàn tất khám (UC38) sẽ tiếp tục mở rộng trên nền `MedicalRecord` và `AllowedActions` đã thiết lập tại ADR này.
