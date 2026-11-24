# 0051 — Hợp đồng API & Cổng Bệnh nhân xem Hồ sơ bệnh án điện tử EMR (`#817` / `#151`–`#154`, `#157` / `UC19`–`UC21`)

- Ngày: 2026-11-25
- Trạng thái: accepted
- Lane: high-risk (PHI / phân quyền 2 lớp / audit trail)
- Liên quan: `0031` (complete visit), `0035` (patient diagnostic result), `0045` (family link PHI scope), `0046` (patient prescription view)

## Context

`UC19` yêu cầu bệnh nhân tra cứu danh sách và chi tiết hồ sơ bệnh án điện tử (EMR) sau khi bác sĩ hoàn tất lượt khám (`UC38`), đồng thời kết nối liền mạch sang kết quả cận lâm sàng (`UC20`/`UC35`) và đơn thuốc ngoại trú (`UC21`/`#158`). Hiện tại thanh điều hướng bệnh nhân (`nav-links.ts`, `user-menu.tsx`) đã trỏ tới `/emr` và `/settings` nhưng chưa có route và backend chưa có endpoint EMR dành riêng cho vai trò `PATIENT` (API `MedicalHistoryService` hiện hữu chỉ phục vụ `DOCTOR` trong phạm vi phụ trách lâm sàng).

## Decision

1. **Endpoints mới trên `PatientsController` (`[Authorize(Roles = "PATIENT")]`)**:
   - `GET /api/v1/patients/me/medical-records` — danh sách hồ sơ bệnh án đã hoàn tất có phân trang (`page`, `pageSize` tối đa 50) và bộ lọc theo thành viên gia đình (`patientId`), khoảng ngày khám (`fromDate`, `toDate` định dạng `yyyy-MM-dd`), cơ sở y tế (`hospitalId`), chuyên khoa (`specialtyId`, `specialtyCode`) và từ khóa (`keyword`).
   - `GET /api/v1/patients/me/medical-records/{idOrCode}` — chi tiết hồ sơ bệnh án theo `medical_records.id`, mã `EMR-{id}`, hoặc `visits.visit_code`.

2. **Phân quyền 2 lớp chuẩn PHI (`WherePhiMemberOf`)**:
   - Layer 1: `[Authorize(Roles = RoleNames.Patient)]` chặn người dùng chưa đăng nhập (`401`) hoặc khác vai trò `PATIENT` (`403`).
   - Layer 2: Kiểm tra tài khoản `User` đang `ACTIVE`, chưa bị khóa (`LockedUntil`), và nạp phạm vi hồ sơ qua `AppointmentBookingRules.LoadOwnedPatientsPhiAsync` (`PatientAccessScope.LoadActiveSelfAsync` + `WherePhiMemberOf`).
   - Chỉ cho phép hồ sơ chính chủ (`ACTIVE`) và liên kết gia đình đã xác minh độc lập (`verification_status = 'VERIFIED'` và `verification_method IN ('OTP', 'STAFF')`, chưa thu hồi, còn hạn). Liên kết `LEGACY`, `PENDING`, `REVOKED` hoặc hồ sơ người lạ bị từ chối với `ForbiddenException` (`403`).

3. **Điều kiện công bố bệnh án cho Bệnh nhân**:
   - Chỉ trả về lượt khám đã hoàn tất (`visits.status = 'COMPLETED'`) gắn với bệnh án đã khóa (`medical_records.status = 'FINALIZED'` hoặc có `finalized_at`), không ở trạng thái `CANCELLED`/`NO_SHOW`.
   - Nội dung lâm sàng lấy từ phiên bản xác nhận mới nhất (`medical_record_revisions.is_confirmed = true`, fallback `revision_no` cao nhất), không lộ bản nháp nội bộ.
   - Tóm tắt cận lâm sàng trong chi tiết bệnh án chỉ hiển thị kết luận tóm tắt khi chỉ định đã được công bố cho bệnh nhân (cùng điều kiện finalized của `PatientDiagnosticResultService`).
   - Tóm tắt đơn thuốc chỉ lấy các đơn ở trạng thái `ISSUED`, `PREPARING`, `READY`, `DISPENSED` chưa bị thay thế bởi đơn mới hơn; cung cấp cờ `canViewPrescriptionDetail` (`isSelfProfile`) để đồng bộ với phạm vi chính chủ của `ADR-0046`.

4. **Audit Trail (`audit_logs`)**:
   - Mọi truy vấn hợp lệ tới danh sách và chi tiết EMR đều ghi `AuditLog` với `Action = "PATIENT_MEDICAL_RECORD_VIEWED"` qua `IAuditLogger`, kèm metadata định danh (`visitId`, `patientId`, `scope`, `resultCount`) và `ipAddress`, tuyệt đối không ghi văn bản lâm sàng hoặc PII nhạy cảm vào `change_data`.

5. **Frontend Patient Portal (`src/features/patient-records/`)**:
   - Triển khai `MedicalRecordListPage` (`/emr`, `/patient/emr`), `MedicalRecordDetailPage` (`/emr/:recordId`, `/patient/emr/:recordId`), `PatientPrescriptionListPage` (`/patient/prescriptions`, `/emr/prescriptions`), `PatientPrescriptionDetailPage` (`/emr/prescriptions/:prescriptionCode`, `/patient/prescriptions/:prescriptionCode`), và `AccountSettingsPage` (`/settings`, `/patient/settings`) theo chuẩn Patient Portal Shell và semantic tokens.

## Consequences

- Khép kín trọn vẹn luồng bệnh nhân xem EMR, kết quả CLS, đơn thuốc và cài đặt tài khoản mà không cần thay đổi schema CSDL hay sửa các interface dùng chung (`IApplicationDbContext`, `PermissionCodes`).
- Ngăn chặn rò rỉ dữ liệu PHI qua liên kết gia đình `LEGACY`/chưa xác minh và đảm bảo đầy đủ dấu vết kiểm toán `PATIENT_MEDICAL_RECORD_VIEWED`.
