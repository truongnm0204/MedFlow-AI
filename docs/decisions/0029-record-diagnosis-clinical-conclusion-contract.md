# 0029 — Record Diagnosis & Clinical Conclusion Contract (`#112` / `M3-004` / UC36)

- Ngay: 2026-04-20
- Trang thai: accepted
- Lane: high-risk

## Context

Sau khi Bác sĩ mở phiên khám lâm sàng (`POST /api/v1/doctor/consultation/{idOrCode}/open` — UC31, ADR 0022), ghi nhận sinh hiệu & khám lâm sàng (`POST|PUT /api/v1/doctor/consultation/{idOrCode}/clinical-examination` — UC33, ADR 0025), và có đầy đủ kết quả cận lâm sàng bắt buộc (UC35), Bác sĩ thực hiện **UC36 — Record Diagnosis & Clinical Conclusion** (`#112` / `M3-004`) để:
1. Tra cứu danh mục mã bệnh ICD-10 (`icd10_codes`) được phép chọn (`is_selectable = true`) và lọc mã hợp lệ cho chẩn đoán chính (`not_primary_dx = false`).
2. Ghi nhận hoặc cập nhật 1 chẩn đoán bệnh chính (`visit_diagnoses.is_primary = true`) và tối đa 10 chẩn đoán bệnh kèm theo (`is_primary = false`), lưu ảnh chụp mã/tên/phiên bản danh mục (`code_snapshot`, `name_snapshot`, `catalog_version`), chẩn đoán lâm sàng (`clinical_diagnosis`), trạng thái (`PRELIMINARY` / `CONFIRMED`) và thời điểm xác nhận (`confirmed_at`).
3. Ghi nhận kết luận lâm sàng (`medical_record_revisions.clinical_conclusion`) theo cơ chế bất biến phiên bản (append-only `medical_record_revisions`), kiểm tra sẵn sàng kết quả cận lâm sàng bắt buộc (`UC35`), kiểm soát xung đột đồng thời (`ExpectedStateVersion`, `ExpectedRevisionNo`), và ghi nhật ký kiểm toán PHI (`DIAGNOSIS_RECORDED`).

## Decision

1. **API Endpoints (`POST` / `PUT` / `GET`)**:
   - Ghi nhận chẩn đoán & kết luận lâm sàng:
     - `POST|PUT /api/v1/doctor/consultation/{idOrCode}/diagnosis`
     - Các alias tương thích: `POST|PUT /api/v1/doctor/consultation/{idOrCode}/diagnoses`, `POST|PUT /api/v1/doctor/visits/{idOrCode}/diagnosis`, `POST|PUT /api/v1/visits/{idOrCode}/diagnosis`.
   - Tra cứu danh mục ICD-10:
     - `GET /api/v1/doctor/icd10-codes`, `GET /api/v1/doctor/consultation/icd10-codes`, `GET /api/v1/icd10-codes` (`?q=&primaryOnly=&limit=`).
   - Hỗ trợ cả hai định dạng payload ghi nhận chẩn đoán:
     - Danh sách có cấu trúc (`diagnoses: [{ icd10CodeId, code, name, isPrimary, clinicalDiagnosis, status }]`).
     - Các trường phẳng tương thích trực tiếp với form UI (`primaryIcd10CodeId`, `primaryIcd10Code`, `primaryClinicalDiagnosis`, `secondaryIcd10CodeIds`, `secondaryIcd10Codes`, `clinicalConclusion`, `differentialNotes`, `lifestyleInstructions`, `warningSigns`, `amendmentReason`, `diagnosisStatus`, `isConfirmed`).

2. **Kiểm tra dữ liệu đầu vào & Ràng buộc danh mục ICD-10 (`400 BadRequest`)**:
   - Bắt buộc có đúng 1 chẩn đoán chính (`isPrimary = true`) và tối đa 10 chẩn đoán bệnh kèm theo (`isPrimary = false`).
   - Không cho phép trùng lặp mã ICD-10 (`code`) hoặc ID danh mục (`icd10CodeId`) trong cùng một lượt khám.
   - Chuẩn hóa mã ICD-10 in hoa và kiểm tra định dạng `^[A-Z][0-9]{2}(\.[0-9A-Z]{1,4})?$`.
   - Khi bảng `icd10_codes` có dữ liệu:
     - Mã/ID phải tồn tại trong danh mục và khớp nhau nếu truyền cả hai.
     - Từ chối mã nhóm/chương không được chọn trực tiếp (`is_selectable = false`).
     - Từ chối mã có `not_primary_dx = true` khi được chọn làm chẩn đoán chính (`isPrimary = true`).
   - Trạng thái chẩn đoán chỉ chấp nhận `PRELIMINARY` hoặc `CONFIRMED`.
   - Kiểm tra độ dài và chặn ký tự `<` / `>` trên tất cả trường văn bản lâm sàng (`ClinicalConclusion` tối thiểu 3 ký tự, tối đa 2000 ký tự).

3. **Bảo mật 2 lớp, Điều kiện sẵn sàng CLS & Máy trạng thái lâm sàng (`401` / `403` / `409`)**:
   - Layer 1: `[Authorize(Roles = "DOCTOR")]` tại `DoctorConsultationController`.
   - Layer 2 tại `ConsultationWorkspaceService`: xác thực `User`/`Staff`/`Doctor`/`Hospital` đang hoạt động, role `DOCTOR`, quyền RBAC `MEDICAL_RECORD_WRITE` (cho thao tác ghi), và quyền sở hữu ca khám/buồng khám (`visit.AssignedDoctorId == actorDoctor.Id || servicePoint.DoctorId == actorDoctor.Id` trong cùng bệnh viện).
   - Điều kiện tiên quyết cận lâm sàng (`409 Conflict` — UC35):
     - Từ chối kết luận chẩn đoán khi ca khám đang ở trạng thái chờ kết quả (`AWAITING_RESULTS`/`AWAITING_DIAGNOSTIC`), hoặc còn lượt hàng đợi `DIAGNOSTIC` chưa hoàn tất khi `final_result_required = true`, hoặc còn chỉ định cận lâm sàng bắt buộc (`diagnostic_orders.required_for_conclusion = true`) chưa có trạng thái hoàn tất (`COMPLETED`/`DONE`/`FINALIZED`/`VERIFIED`/`CANCELLED`).
   - Ca khám và hàng đợi lâm sàng phải đang ở trạng thái `IN_PROGRESS`, hồ sơ bệnh án chưa bị khóa (`medical_records.status != 'FINALIZED'`), và thỏa mãn kiểm tra đồng thời `ExpectedStateVersion` / `ExpectedRevisionNo`.

4. **Đồng bộ `visit_diagnoses`, Append-Only `medical_record_revisions` & Audit Trail**:
   - Đồng bộ tập `visit_diagnoses` của `visit_id`: cập nhật tại chỗ các mã giữ lại (bảo toàn `confirmed_at` nếu đã xác nhận trước đó), thêm mới các mã bổ sung, và xóa các mã bị loại bỏ.
   - Tạo một bản ghi mới trong `medical_record_revisions` (`revision_no = currentRevisionNo + 1`), kế thừa triệu chứng/tiền sử/sinh hiệu/khám lâm sàng từ revision gần nhất và cập nhật `clinical_conclusion`.
   - Ghi `AuditLog` với `Action = "DIAGNOSIS_RECORDED"`, `EntityType = "MEDICAL_RECORD"`, `EntityId = medicalRecord.Id`, lưu chi tiết danh sách chẩn đoán mới/cũ, kết luận lâm sàng, ghi chú phân biệt, dặn dò và dấu hiệu cảnh báo.
   - Trả về `ConsultationWorkspaceResponseDto` (`actionOutcome = "DIAGNOSIS_RECORDED"`).

## Consequences

- **Duoc**:
  - Đáp ứng trọn vẹn nghiệp vụ UC36 mà không cần thay đổi schema hay thêm SQL migration mới.
  - Đảm bảo tính toàn vẹn dữ liệu danh mục ICD-10 (`is_selectable`, `not_primary_dx`) và chặn kết luận sớm khi cận lâm sàng bắt buộc chưa hoàn tất.
  - Phản hồi tức thì toàn bộ `ConsultationWorkspaceResponseDto` giúp giao diện Bác sĩ đồng bộ ngay danh sách chẩn đoán và số phiên bản hồ sơ bệnh án.
- **Mat**:
  - Mỗi lần cập nhật chẩn đoán và kết luận lâm sàng tạo thêm một dòng `medical_record_revisions` để đảm bảo tính bất biến lịch sử bệnh án.
- **Rui ro con lai**:
  - Khi hoàn tất lượt khám (UC38), dịch vụ đóng ca khám cần kiểm tra đã có ít nhất 1 chẩn đoán chính xác nhận trước khi khóa `medical_records.status = 'FINALIZED'`.
