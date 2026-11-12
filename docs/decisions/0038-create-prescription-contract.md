# 0038 — Create Prescription Contract (`#140` / `M3-032` / UC37)

- Ngay: 2026-11-12
- Trang thai: accepted
- Lane: high-risk

## Context

Sau khi Bác sĩ đã mở phiên khám (`UC31`) và ghi nhận chẩn đoán bệnh chính ICD-10 (`UC36`, ADR 0029), Bác sĩ kê đơn thuốc ngoại trú cho lượt khám (`UC37` / `#140` / `M3-032`). Đơn được lưu ở trạng thái nháp. `UC38` (ADR 0031) phát hành đơn nháp hợp lệ khi hoàn tất lượt khám (`DRAFT`/`PENDING` → `ISSUED`).

Schema `v4.1` đã có `prescriptions` và `prescription_items`. Không có unique/CHECK constraint trên đơn thuốc, và không có cột ghi chú cấp đơn. Toàn bộ ràng buộc nằm ở application. Không migration.

## Decision

1. **API Endpoints (`POST` / `PUT`)**:
   - `POST|PUT /api/v1/doctor/consultation/{idOrCode}/prescriptions`
   - `POST|PUT /api/v1/doctor/consultation/{idOrCode}/prescription`
   - `POST|PUT /api/v1/doctor/visits/{idOrCode}/prescriptions`
   - `POST|PUT /api/v1/visits/{idOrCode}/prescriptions`
   - Body `CreatePrescriptionRequest` (`items`, `fulfillmentChoice`, `expectedStateVersion`, `expectedRevisionNo`). Body rỗng được phép và trả `400`.
   - Response: `ConsultationWorkspaceResponseDto`. `clinicalContext.prescriptionSummary` đọc lại từ DB (`BuildPrescriptionSummaryAsync`).

2. **Request item**: `medicineId` và/hoặc `medicineCode`, `prescribedQuantity` bắt buộc, `singleDoseQuantity`, `singleDoseUnit`, `doseScheduleText`, `frequency`, `durationDays`, `administrationRoute`, `instructions`. Không thêm trường ghi chú cấp đơn.

3. **Kiểm tra dữ liệu đầu vào (`400 BadRequest`) — chạy trước mọi truy cập DB**:
   - Body null: "Dữ liệu đơn thuốc không được để trống."
   - `ExpectedStateVersion >= 1`, `ExpectedRevisionNo >= 1` khi được truyền.
   - `FulfillmentChoice`: trim + uppercase; null/blank → `HOSPITAL`. Chỉ `HOSPITAL`, `HOSPITAL_PHARMACY`, `EXTERNAL_PHARMACY`.
   - `items` null hoặc rỗng → từ chối. Tối đa 20 dòng.
   - Mỗi dòng: không null; `medicineId` nếu có thì `> 0`; `medicineCode` trim, rỗng = null, tối đa 40 ký tự, không `<`/`>`, lưu uppercase; phải có id hoặc mã.
   - `prescribedQuantity` trong `(0, 9999]`. `singleDoseQuantity` nếu có thì cùng khoảng. `durationDays` nếu có thì `1..365`.
   - Text qua `ValidateClinicalTextField` (chặn `<`/`>`): đơn vị liều 40, tần suất 160, đường dùng 100, lịch dùng 500, hướng dẫn 1000.
   - Sau chuẩn hóa, cả `doseScheduleText` và `instructions` đều trống → từ chối. Một trong hai có giá trị là đủ.
   - Trùng `medicineId` hoặc trùng `medicineCode` đã chuẩn hóa trong cùng request → từ chối.
   - Catalog (sau khi qua guard trạng thái): id và mã cùng dòng phải khớp; mã không duy nhất trong bệnh viện → bắt chọn theo id; không thấy thuốc trong bệnh viện của bác sĩ (kể cả thuốc bệnh viện khác) → `400`; `IsActive == false` → `400`; hai dòng resolve về cùng `Medicine.Id` → `400`.

4. **Bảo mật 2 lớp và máy trạng thái (`401` / `403` / `409`)**:
   - Layer 1: `[Authorize(Roles = "DOCTOR")]` tại `DoctorConsultationController`.
   - Layer 2: `EnsureDoctorAccessAsync(..., PRESCRIPTION_ISSUE)` rồi bắt buộc thêm `MEDICAL_RECORD_WRITE`. `ResolveAndAuthorizeVisitContextAsync` từ chối lượt khám bệnh viện khác hoặc bác sĩ khác (`403`).
   - `409`: lượt hủy/vắng; đã `COMPLETED`/`DONE`; không có hàng đợi lâm sàng hợp lệ; đang chờ CLS hoặc còn CLS mở khi chưa `IN_PROGRESS`; còn kết quả CLS bắt buộc; queue không `IN_PROGRESS` hoặc visit không thuộc `IN_PROGRESS|IN_CONSULTATION|SERVING`; phòng hoặc điểm phục vụ ngừng hoạt động; `state_version` / `revision_no` lệch; chưa có hồ sơ bệnh án; hồ sơ `FINALIZED`; chưa có chẩn đoán chính ICD-10 (`IsPrimary` và `CodeSnapshot` không rỗng).
   - Không đổi `visits.status`, `queue_entries.status`, `state_version`.

5. **Lưu trữ, mã đơn, upsert và audit**:
   - Đơn mới: `Status = DRAFT`, `IssuedAt = null`, `PrescribingDoctorId` = bác sĩ đang khám, `FulfillmentChoice` đã chuẩn hóa, `CreatedAt` = giờ Việt Nam. Sau khi có `Id`, gán `PrescriptionCode = RX-{queueDate:yyyyMM}-{id:D4}` với `queueDate = activeEntry.QueueDate ?? todayLocal` (cùng kiểu hai pha với `DIA-`).
   - Gọi lại khi đã có đơn `DRAFT`/`PENDING`/trống (cùng tập UC38 dùng khi phát hành): thay toàn bộ dòng thuốc, cập nhật fulfillment và bác sĩ kê, giữ `PrescriptionCode` và `CreatedAt`, đặt lại `Status = DRAFT`. Không tạo đơn thứ hai. Mã trống thì gán theo công thức trên.
   - `SingleDoseUnit` trống thì lấy `medicine.DispensingUnit`.
   - Audit `PRESCRIPTION_CREATED` hoặc `PRESCRIPTION_UPDATED`, `EntityType = PRESCRIPTION`, `EntityId` = id đơn. Payload: visit, mã đơn, trạng thái, fulfillment, `items`, `previousItems` (rỗng khi tạo mới), `stateVersion`, `revisionNo`, `ipAddress`.
   - `UC38` đọc đơn nháp này và chuyển `ISSUED` + `IssuedAt` khi hoàn tất lượt khám.

## Consequences

- **Duoc**: UC37 khép vào `ConsultationWorkspaceService` bằng helper authz sẵn có, không nhân bản lớp phân quyền, không migration. Nhà thuốc (`UC44`) vẫn chỉ thấy `HOSPITAL` / `HOSPITAL_PHARMACY`.
- **Mat**: Sửa đơn đã `ISSUED` không đi qua API này. Sau `COMPLETED`/`FINALIZED` mọi ghi đơn trả `409`.
- **Rui ro con lai**: Không chặn dị ứng có cấu trúc — schema chỉ có dị ứng dạng text tự do trên revision. Cảnh báo dị ứng vẫn ở client. Giới hạn 20 dòng / số lượng 9999 / 365 ngày là chặn dữ liệu bẩn, không phải quy tắc nghiệp vụ đã chốt trong tài liệu.
