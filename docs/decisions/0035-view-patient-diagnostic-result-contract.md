# 0035 — Hợp đồng API bệnh nhân xem kết quả cận lâm sàng (`#560` / `M3-048` / `UC35`)

- Ngay: 2026-11-12
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#560` (`M3-048`, parent `UC35 — View Diagnostic Result`) yêu cầu API để bệnh nhân xem kết quả cận lâm sàng và đường dẫn tài liệu/viewer được phép công bố. Nhãn issue ghi `role:diagnostic-staff`, nhưng câu scope và nhánh `feature/m3-560-be-view-patient-diagnostic-result` chỉ bệnh nhân.

Đã có sẵn:

- `#134` / ADR 0033 ghi `diagnostic_results` + `diagnostic_result_versions` (`result_text`, `conclusion`, `report_file_url`, `is_finalized`, `verified_at`).
- `GET /api/v1/diagnostic/results` thuộc `DiagnosticResultsController`, `[Authorize(Roles = "DIAGNOSTIC_STAFF")]`, phục vụ nhập kết quả UC43. Bệnh nhân gọi route này nhận `403`.
- FE `#559` đóng trên mock `PatientDiagnosticResultPage`. Sheet mock có ảnh nội soi, chỉ số CBC, checklist. Schema v4.1 không có các cột đó.
- Quyền hồ sơ bệnh nhân đã có tại `PatientAccessScope` + `AppointmentBookingRules.LoadOwnedPatientsAsync`: bản thân và `patient_family_links` `VERIFIED`, chưa thu hồi, còn hiệu lực.

## Decision

1. **Đối tượng là `PATIENT`.** Không mở route staff hiện có cho bệnh nhân và không thêm action xem vào `DiagnosticResultsController`.
2. **Endpoint mới, gắn `PatientsController`:**
   - `GET /api/v1/patients/me/diagnostic-results` — danh sách kết quả trong phạm vi hồ sơ.
   - `GET /api/v1/patients/me/diagnostic-results/{idOrCode}` — chi tiết một chỉ định theo `diagnostic_orders.id` hoặc `order_code`.
3. **Phạm vi sở hữu** dùng lại `PatientAccessScope.LoadActiveSelfAsync` và `WhereActiveMemberOf`. `patientId` query không thuộc scope → `ForbiddenException` (`403`). Chỉ định tồn tại nhưng `visits.patient_id` ngoài scope → `403`. Không tin id bệnh nhân do client tự khai khi không kiểm scope.
4. **Được công bố** khi cùng điều kiện `DiagnosticResultEntryService.IsDiagnosticResultRowFinalized`: `finalized_at` có giá trị, hoặc `diagnostic_results.status` thuộc `FINALIZED | VERIFIED | COMPLETED`, hoặc phiên bản đang xét có `is_finalized = true` hoặc `verified_at`. Đồng thời order, queue và result không ở `CANCELLED`.
5. **Chưa công bố nhưng thuộc scope** (đã có dòng `diagnostic_results`, chưa finalized): `200` với `isPublishedToPatient = false`. `resultText`, `conclusion`, `reportFileUrl` là `null`. Khớp màn khóa của `PatientDiagnosticResultPage`.
6. **Phiên bản bệnh nhân được đọc** là phiên bản finalized có `version_no` lớn nhất. Bản nháp hoặc đính chính chưa finalized mới hơn không đè nội dung đã công bố. Không trả `correction_reason` và không trả lịch sử phiên bản.
7. **Định danh chi tiết** theo thứ tự của ADR 0005: trim, bỏ `#` đầu, rỗng hoặc dài hơn 40 ký tự hoặc số không parse được sang `long > 0` → `ValidationException` (`400`). Toàn số → tìm theo `id`; còn lại uppercase và tìm theo `order_code`. Không có order, hoặc order không có dòng result → `NotFoundException` (`404`). Order/result/queue `CANCELLED` → `404`.
8. **Payload** chỉ lấy cột đang có: định danh bệnh nhân/lượt khám/chỉ định, tên bệnh viện, tên dịch vụ, tên bác sĩ chỉ định (`doctors` → `staff.full_name`), người thực hiện/ký (`verified_by_staff_id`, fallback `entered_by_staff_id`), tên điểm thực hiện (`queue_service_points.name`, fallback `hospital_rooms.name`), `signedAt` (`verified_at` fallback `finalized_at`), `resultText`, `conclusion`, `reportFileUrl`. Không sinh PDF, không proxy file, không bịa ảnh, chỉ số lab, ICD, dị ứng.
9. **Danh sách** gồm mọi result trong scope, kể cả chưa công bố, trừ cancelled và chỉ định chưa có result. Query: `patientId` optional, `visitId` hoặc `visitCode` optional, `page` mặc định 1, `pageSize` mặc định 20, tối đa 50. Sort `signedAt` giảm dần, rồi `orderId` giảm dần. Danh sách không kèm `resultText` và `reportFileUrl`.
10. **GET không ghi `audit_logs`**, cùng quy ước ADR 0005. Không migration.

## Consequences

- Được: bệnh nhân và hồ sơ người thân đã xác thực đọc đúng kết quả đã ký, không thấy nháp nội bộ, không đụng API staff.
- Mất: FE mock SCR bệnh nhân chưa gắn được gallery ảnh và bảng CBC cho đến khi schema có cấu trúc đó.
- Rủi ro còn lại: đọc PHI không có audit. Sửa kết quả sau công bố (`#136`) chỉ hiện với bệnh nhân sau khi phiên bản mới được finalized.
