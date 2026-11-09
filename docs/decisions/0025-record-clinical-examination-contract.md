# 0025 — Record Clinical Examination Contract (`#110` / `M2-056` / UC33)

- Ngay: 2026-04-20
- Trang thai: accepted
- Lane: high-risk

## Context

Sau khi Bác sĩ mở phiên khám lâm sàng (`POST /api/v1/doctor/consultation/{idOrCode}/open` — UC31, ADR 0022) đưa lượt khám sang trạng thái `IN_PROGRESS` và khởi tạo hồ sơ bệnh án nháp (`medical_records.status = 'DRAFT'`, `medical_record_revisions.revision_no = 1`), Bác sĩ cần thực hiện **UC33 — Record Clinical Examination** (`#110` / `M2-056`) để ghi nhận:
1. Triệu chứng chính / lý do khám (`presenting_complaint`), bệnh sử & diễn tiến triệu chứng (`symptom_history`), tiền sử bệnh & dị ứng liên quan (`relevant_history_allergies`).
2. Các chỉ số sinh hiệu (`vital_signs` JSONB): huyết áp tâm thu/tâm trương (`bp_systolic_mmhg`, `bp_diastolic_mmhg`), mạch (`pulse_bpm`), nhiệt độ (`temperature_c`), SpO2 (`spo2_percent`), chiều cao (`height_cm`), cân nặng (`weight_kg`), và tự động tính nhãn huyết áp (`120/80 mmHg`) cùng chỉ số `BMI`.
3. Khám lâm sàng thực thể (`clinical_findings`), nhận định/kết luận lâm sàng ban đầu (`clinical_conclusion`), và lý do bổ sung/cập nhật (`amendment_reason`).
4. Đảm bảo tính bất biến theo phiên bản (append-only `medical_record_revisions`), kiểm soát đồng thời (`ExpectedStateVersion` và `ExpectedRevisionNo`), phân quyền 2 lớp (`DOCTOR` + `MEDICAL_RECORD_WRITE` + quyền sở hữu ca khám/buồng khám), và ghi nhận nhật ký kiểm toán PHI (`CLINICAL_EXAMINATION_RECORDED`).

## Decision

1. **API Endpoints & Hỗ trợ cập nhật linh hoạt (`POST` / `PUT`)**:
   - Cung cấp endpoint chính `POST|PUT /api/v1/doctor/consultation/{idOrCode}/clinical-examination` cùng các alias tương thích `/api/v1/doctor/visits/{idOrCode}/clinical-examination` và `/api/v1/visits/{idOrCode}/clinical-examination`.
   - Cho phép định danh ca khám qua `visit_id`, `visit_code` (`VIS-...`), hoặc `queue_entry_id` (`q-011`).
   - Hỗ trợ truyền chỉ số sinh hiệu theo cả cấu trúc lồng (`vitalSigns: { ... }`) hoặc phẳng ở cấp cao nhất của payload (`bpSystolicMmhg`, `bpDiastolicMmhg`, ...), ưu tiên giá trị trong `vitalSigns` khi cả hai cùng xuất hiện.

2. **Kiểm tra dữ liệu đầu vào (Clinical & Vital Sign Validation — `400 BadRequest`)**:
   - Từ chối payload rỗng hoặc không có bất kỳ trường văn bản lâm sàng hay chỉ số sinh hiệu nào.
   - Ràng buộc sinh hiệu:
     - Huyết áp tâm thu (`40–300 mmHg`) và tâm trương (`20–200 mmHg`) phải đi theo cặp và `bp_systolic_mmhg > bp_diastolic_mmhg`.
     - Mạch (`20–300 bpm`), nhiệt độ (`30.0–45.0 °C`), SpO2 (`50–100%`), chiều cao (`30.0–250.0 cm`), cân nặng (`1.0–500.0 kg`).
   - Ràng buộc trường văn bản: loại bỏ khoảng trắng thừa, chặn ký tự `<` và `>`, giới hạn độ dài (`PresentingComplaint <= 1000`, `SymptomHistory <= 4000`, `RelevantHistoryAllergies <= 2000`, `ClinicalFindings <= 4000`, `ClinicalConclusion <= 2000`, `AmendmentReason <= 500`).
   - Kiểm tra `ExpectedStateVersion >= 1` và `ExpectedRevisionNo >= 1` khi được truyền.

3. **Bảo mật 2 lớp & Máy trạng thái lâm sàng (`401` / `403` / `409`)**:
   - Layer 1: `[Authorize(Roles = "DOCTOR")]` tại `DoctorConsultationController`.
   - Layer 2 tại `ConsultationWorkspaceService`: xác thực `User` (`ACTIVE`, không khóa), role `DOCTOR`, `Staff` (`ACTIVE`, có `HospitalId`), `Doctor` (`IsActive`, cùng `HospitalId`), `Hospital` (`IsActive`), quyền RBAC `MEDICAL_RECORD_WRITE`, và quyền sở hữu ca khám (`visit.AssignedDoctorId == actorDoctor.Id || servicePoint.DoctorId == actorDoctor.Id` trong cùng bệnh viện).
   - State invariants (`409 Conflict`):
     - Ca khám/hàng đợi phải đang ở trạng thái `IN_PROGRESS` (từ chối `WAITING`, `READY`, `CALLED`, `SKIPPED`, `AWAITING_RESULTS`, `COMPLETED`, `CANCELLED`, `NO_SHOW`).
     - Phòng khám và điểm phục vụ phải đang hoạt động (`ACTIVE` / `IsActive == true`).
     - Hồ sơ bệnh án chưa bị khóa (`medical_records.status != 'FINALIZED'`).
     - Kiểm soát xung đột đồng thời qua `ExpectedStateVersion` (so với `QueueEntry.StateVersion`) và `ExpectedRevisionNo` (so với `latestRevision.RevisionNo`).

4. **Lưu trữ phiên bản bất biến (Append-Only Revisions) & Audit Trail**:
   - Mỗi lần ghi nhận tạo một dòng mới trong `medical_record_revisions` với `revision_no = currentRevisionNo + 1`, kế thừa các trường văn bản và chỉ số sinh hiệu từ phiên bản gần nhất nếu request chỉ cập nhật một phần (partial merge).
   - Ghi `AuditLog` với `Action = "CLINICAL_EXAMINATION_RECORDED"`, `EntityType = "MEDICAL_RECORD"`, `EntityId = medicalRecord.Id`, lưu metadata phiên bản (`previousRevisionNo`, `revisionNo`, `isConfirmed`, `hasVitalSigns`, `stateVersion`).
   - Trả về `ConsultationWorkspaceResponseDto` với `actionOutcome = "CLINICAL_EXAMINATION_RECORDED"`, phản ánh ngay phiên bản mới nhất, sinh hiệu kèm `bloodPressureLabel` và `bmi`, đồng thời đồng bộ `patient.allergiesSummary` từ `relevantHistoryAllergies`.

## Consequences

- **Duoc**:
  - Lưu vết đầy đủ lịch sử chỉnh sửa khám lâm sàng theo đúng thiết kế append-only `medical_record_revisions` trong `medflow_schema_v4_1.sql` mà không cần migration SQL mới.
  - Phản hồi tức thì toàn bộ trạng thái Consultation Workspace (`ConsultationWorkspaceResponseDto`) giúp frontend cập nhật UI trong 1 round-trip.
  - Chặn chặt chẽ các truy cập trái phép giữa các bác sĩ/bệnh viện và các thao tác ghi sai trạng thái hàng đợi.
- **Mat**:
  - Mỗi lần lưu khám lâm sàng sinh thêm một bản ghi `medical_record_revisions`, tăng nhẹ dung lượng lưu trữ đổi lấy khả năng truy vết pháp lý y khoa.
- **Rui ro con lai**:
  - Khi hoàn tất lượt khám (UC38), dịch vụ đóng hồ sơ sẽ chuyển `medical_records.status` sang `FINALIZED` để khóa vĩnh viễn việc tạo thêm revision nháp qua UC33.
