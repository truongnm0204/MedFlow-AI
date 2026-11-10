# 0031 — Complete Visit Contract (`#118` / `M3-010` / UC38)

- Ngay: 2026-11-10
- Trang thai: accepted
- Lane: high-risk

## Context

Sau khi Bác sĩ mở phiên khám lâm sàng (`UC31`, ADR 0022), ghi nhận sinh hiệu & khám lâm sàng (`UC33`, ADR 0025), xử lý chỉ định và nhận đầy đủ kết quả cận lâm sàng (`UC34`/`UC35`, ADR 0030), ghi nhận chẩn đoán bệnh chính ICD-10 và kết luận lâm sàng (`UC36`, ADR 0029), cùng đơn thuốc ngoại trú nếu có (`UC37`), Bác sĩ thực hiện **UC38 — Complete Visit** (`#118` / `M3-010`) để:
1. Thẩm định tính đầy đủ của thông tin lâm sàng bắt buộc (hồ sơ khám lâm sàng, không còn bước cận lâm sàng đang chờ xử lý, đã có ít nhất 1 chẩn đoán chính ICD-10 và kết luận lâm sàng, đơn thuốc nháp hợp lệ).
2. Xác nhận chính thức toàn bộ chẩn đoán (`visit_diagnoses.status = 'CONFIRMED'`), khóa vĩnh viễn hồ sơ bệnh án (`medical_records.status = 'FINALIZED'`, `finalized_at`), và phát hành đơn thuốc nháp hợp lệ (`prescriptions.status = 'ISSUED'`).
3. Chuyển trạng thái lượt xếp hàng lâm sàng (`queue_entries.status = 'COMPLETED'`, `completed_at`, tăng `state_version`) và lượt khám (`visits.status = 'COMPLETED'`, `completed_at`, `completed_by_doctor_id`) trong cùng một giao dịch, đồng thời ghi nhật ký kiểm toán PHI (`VISIT_COMPLETED`).

## Decision

1. **API Endpoints (`POST` / `PUT`)**:
   - Endpoint chính: `POST|PUT /api/v1/doctor/consultation/{idOrCode}/complete`
   - Các alias tương thích:
     - `POST|PUT /api/v1/doctor/consultation/{idOrCode}/complete-visit`
     - `POST|PUT /api/v1/doctor/visits/{idOrCode}/complete`
     - `POST|PUT /api/v1/doctor/visits/{idOrCode}/complete-visit`
     - `POST|PUT /api/v1/visits/{idOrCode}/complete`
     - `POST|PUT /api/v1/visits/{idOrCode}/complete-visit`
   - Hỗ trợ gọi không truyền body (`null` / `{}`) hoặc truyền `CompleteVisitRequest` (`clinicalConclusion`, `dischargeInstructions`, `followUpInstructions`, `followUpDate`, `warningSigns`, `notes`/`completionNote`, `confirmNoPrescription`, `expectedStateVersion`, `expectedRevisionNo`).

2. **Kiểm tra dữ liệu đầu vào (`400 BadRequest`)**:
   - Kiểm tra định danh lượt khám (`visit_id`, `visit_code` hoặc `queue_entry_id` dạng `q-011`).
   - `ExpectedStateVersion >= 1` và `ExpectedRevisionNo >= 1` khi được truyền.
   - Kiểm tra độ dài và chặn ký tự `<` / `>` trên tất cả trường văn bản (`ClinicalConclusion` 3..2000 ký tự, `DischargeInstructions <= 2000`, `FollowUpInstructions <= 1000`, `WarningSigns <= 1000`, `Notes`/`CompletionNote <= 500`).
   - `FollowUpDate` (nếu có) phải sau ngày khám hiện tại (`> todayLocal`).

3. **Bảo mật 2 lớp, Điều kiện hoàn tất lâm sàng & Máy trạng thái (`401` / `403` / `404` / `409`)**:
   - Layer 1: `[Authorize(Roles = "DOCTOR")]` tại `DoctorConsultationController`.
   - Layer 2 tại `ConsultationWorkspaceService`: xác thực `User`/`Staff`/`Doctor`/`Hospital` đang hoạt động, role `DOCTOR`, quyền RBAC `MEDICAL_RECORD_WRITE`, và quyền sở hữu ca khám/buồng khám trong cùng bệnh viện (`visit.AssignedDoctorId == actorDoctor.Id || servicePoint.DoctorId == actorDoctor.Id`).
   - Điều kiện trạng thái và toàn vẹn quy trình (`409 Conflict`):
     - Ca khám và lượt xếp hàng lâm sàng phải đang ở trạng thái `IN_PROGRESS` (từ chối `WAITING`, `READY`, `CALLED`, `SKIPPED`, `AWAITING_RESULTS`, `AWAITING_DIAGNOSTIC`, `COMPLETED`, `CANCELLED`, `NO_SHOW`).
     - Phòng khám và điểm phục vụ phải đang hoạt động (`ACTIVE` / `IsActive == true`).
     - Không còn bất kỳ lượt xếp hàng cận lâm sàng (`queue_type = 'DIAGNOSTIC'`) hay phiếu chỉ định cận lâm sàng (`diagnostic_orders`) nào đang chờ thực hiện hoặc chưa hoàn tất kết quả.
     - Hồ sơ bệnh án (`medical_records`) đã được khởi tạo, chưa bị khóa (`status != 'FINALIZED'`), đã có phiên bản khám lâm sàng (`medical_record_revisions`), thỏa mãn kiểm tra đồng thời `ExpectedStateVersion` và `ExpectedRevisionNo`.
     - Đã có ít nhất 1 chẩn đoán chính ICD-10 (`visit_diagnoses.is_primary = true`) và có kết luận lâm sàng (`clinical_conclusion`).
     - Nếu có đơn thuốc ở trạng thái nháp (`DRAFT`/`PENDING`), đơn thuốc phải có ít nhất 1 dòng thuốc (`prescription_items`).

4. **Lưu trữ Giao dịch, Khóa Bệnh án & Audit Trail**:
   - Xác nhận toàn bộ chẩn đoán của lượt khám (`visit_diagnoses.status = 'CONFIRMED'`, `confirmed_at ??= nowLocal`).
   - Nếu bác sĩ cập nhật kết luận lâm sàng hoặc ghi chú hoàn tất, tạo thêm phiên bản xác nhận cuối cùng trong `medical_record_revisions` (`is_confirmed = true`); ngược lại đánh dấu `latestRevision.IsConfirmed = true`.
   - Khóa hồ sơ bệnh án (`medical_records.status = 'FINALIZED'`, `finalized_at = nowLocal`).
   - Phát hành các đơn thuốc nháp hợp lệ (`prescriptions.status = 'ISSUED'`, `issued_at ??= nowLocal`).
   - Chuyển `queue_entries.status = 'COMPLETED'`, `completed_at = nowLocal`, tăng `state_version`, và chuyển `visits.status = 'COMPLETED'`, `completed_at = nowLocal`, `completed_by_doctor_id = actorDoctor.Id`.
   - Ghi `AuditLog` với `Action = "VISIT_COMPLETED"`, `EntityType = "VISIT"`, `EntityId = visit.VisitCode ?? visit.Id`, lưu đầy đủ metadata lâm sàng và trạng thái trước/sau.
   - Trả về `ConsultationWorkspaceResponseDto` (`actionOutcome = "VISIT_COMPLETED"`, `workspaceState = "COMPLETED"`, `canEditClinical = false`).

## Consequences

- **Duoc**:
  - Khép kín trọn vẹn vòng đời khám lâm sàng ngoại trú của Bác sĩ (`UC31 -> UC33 -> UC34/UC35 -> UC36 -> UC37 -> UC38`) trên schema `v4.1` hiện hữu mà không cần migration SQL mới.
  - Bảo vệ tính toàn vẹn pháp lý của hồ sơ bệnh án (`FINALIZED`) và chặn đóng ca khám sớm khi còn bước cận lâm sàng hoặc thiếu chẩn đoán chính ICD-10.
  - Đồng bộ tức thì với các phân hệ downstream: Hồ sơ bệnh án bệnh nhân (`UC19`), Nhà thuốc cấp phát (`UC44`–`UC46`), Lịch sử khám (`UC32`), Theo dõi trạng thái lịch hẹn/hàng đợi (`UC15`/`UC16`), và Đặt lịch mới cùng ngày sau khi lượt khám trước đã `COMPLETED` (ADR 0003).
- **Mat**:
  - Sau khi hoàn tất (`FINALIZED` / `COMPLETED`), bác sĩ không thể sửa trực tiếp qua các API nháp (`UC33`/`UC34`/`UC36`), đảm bảo tính bất biến của bệnh án đã khóa.
- **Rui ro con lai**:
  - Mọi đính chính phát sinh sau khi khóa hồ sơ sẽ đi qua cơ chế phụ lục đính chính (`clinical_addenda`).
