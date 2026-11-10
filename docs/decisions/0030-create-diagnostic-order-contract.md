# 0030 — Create Diagnostic Order Contract (`#120` / `M3-012` / UC34)

- Ngay: 2026-04-20
- Trang thai: accepted
- Lane: high-risk

## Context

Trong phiên khám lâm sàng ngoại trú đang diễn ra (`IN_PROGRESS`, sau khi mở không gian khám qua `UC31` và ghi nhận khám lâm sàng qua `UC33`), Bác sĩ cần thực hiện **UC34 — Create Diagnostic Order** (`#120` / `M3-012`) để chỉ định các dịch vụ cận lâm sàng (xét nghiệm `LABORATORY`, chẩn đoán hình ảnh `IMAGING`, thăm dò chức năng `FUNCTIONAL`):
1. Tra cứu danh mục dịch vụ cận lâm sàng thuộc cơ sở y tế của Bác sĩ kèm đơn giá hiện hành (`service_prices`), trạng thái phân quyền dịch vụ (`doctor_service_authorizations`), buồng/điểm phục vụ cận lâm sàng (`queue_service_points`, `hospital_rooms`), và danh sách phiếu chỉ định đã tạo trong lượt khám.
2. Tạo một hoặc nhiều phiếu chỉ định cận lâm sàng (`diagnostic_orders`) gắn với lượt xếp hàng cận lâm sàng (`queue_entries` với `queue_type = 'DIAGNOSTIC'`, `status = 'WAITING'`), kiểm soát đồng thời (`ExpectedStateVersion`), phân quyền 2 lớp (`DOCTOR` + `DIAGNOSTIC_ORDER` + quyền sở hữu ca khám + phân quyền dịch vụ), và ghi nhận nhật ký kiểm toán (`DIAGNOSTIC_ORDER_CREATED`).
3. Đồng bộ tức thì `DiagnosticSummary` (`AllMandatoryResultsReady`, `OpenQueueEntries`) và trạng thái các hành động tiếp theo (`record-diagnosis`, `create-prescription`, `complete-visit`), đồng thời hỗ trợ tùy chọn chuyển ca khám sang trạng thái chờ kết quả (`TransitionToAwaitingResults = true`).

## Decision

1. **API Endpoints & Hỗ trợ đơn lẻ / hàng loạt (`GET` / `POST`)**:
   - Cung cấp `GET /api/v1/doctor/consultation/{idOrCode}/diagnostic-orders` (cùng các alias `/api/v1/doctor/consultation/{idOrCode}/diagnostic-services`, `/api/v1/doctor/visits/{idOrCode}/diagnostic-orders`, `/api/v1/visits/{idOrCode}/diagnostic-orders`) trả về `DiagnosticOrderCatalogResponseDto`.
   - Cung cấp `POST /api/v1/doctor/consultation/{idOrCode}/diagnostic-orders` (cùng các alias `/diagnostic-order`, `/api/v1/doctor/visits/{idOrCode}/diagnostic-orders`, `/api/v1/visits/{idOrCode}/diagnostic-orders`) nhận `CreateDiagnosticOrderRequest`, hỗ trợ cả truyền trực tiếp 1 dịch vụ ở cấp cao nhất (`serviceId` / `serviceCode`) hoặc danh sách nhiều dịch vụ (`items`, tối đa 10 dịch vụ/lần).

2. **Kiểm tra dữ liệu đầu vào (`400 BadRequest`)**:
   - Từ chối payload rỗng, không có dịch vụ cận lâm sàng, hoặc trùng lặp dịch vụ trong cùng một lần chỉ định.
   - Ràng buộc `ServiceId > 0` hoặc `ServiceCode` hợp lệ (`<= 40` ký tự, không chứa `<` hoặc `>`), dịch vụ phải có `service_type = 'DIAGNOSTIC'`.
   - Chuẩn hóa mức độ ưu tiên chỉ định cận lâm sàng (`Priority`): `NORMAL` (mặc định, chấp nhận `STANDARD`/`ROUTINE`), `URGENT`, `STAT`.
   - Ràng buộc `Instruction <= 1000` ký tự, `ClinicalNote <= 500` ký tự (loại bỏ khoảng trắng thừa, chặn `<` và `>`), và `ExpectedStateVersion >= 1` khi được truyền.

3. **Bảo mật 2 lớp, Phân quyền dịch vụ & Máy trạng thái (`401` / `403` / `404` / `409`)**:
   - Layer 1: `[Authorize(Roles = "DOCTOR")]` tại `DoctorConsultationController`.
   - Layer 2 tại `ConsultationWorkspaceService`: xác thực `User`, role `DOCTOR`, `Staff`, `Doctor`, `Hospital`, quyền RBAC `DIAGNOSTIC_ORDER`, quyền sở hữu lượt khám/buồng khám trong cùng bệnh viện, và kiểm tra bảng `doctor_service_authorizations` (nếu bác sĩ hoặc dịch vụ có cấu hình phân quyền thì bắt buộc phải có dòng `is_active = true`).
   - State & Conflict guards (`409 Conflict`):
     - Ca khám và lượt xếp hàng lâm sàng phải đang ở trạng thái `IN_PROGRESS` và hồ sơ bệnh án chưa bị khóa (`medical_records.status != 'FINALIZED'`).
     - Dịch vụ cận lâm sàng, khoa phụ trách, điểm phục vụ cận lâm sàng (`PointType = "DIAGNOSTIC"`) và phòng thực hiện (`HospitalRoom`) phải đang hoạt động (`ACTIVE` / `IsActive == true`).
     - Chặn tạo trùng phiếu chỉ định đang mở (`status != 'COMPLETED' && status != 'CANCELLED'`) cho cùng một dịch vụ trong cùng lượt khám.
     - Kiểm soát xung đột đồng thời qua `ExpectedStateVersion` và tăng `ActiveClinicalEntry.StateVersion` sau khi tạo chỉ định.

4. **Lưu trữ Giao dịch (`QueueEntry` + `DiagnosticOrder` + `AuditLog`)**:
   - Với mỗi dịch vụ cận lâm sàng được chỉ định, khởi tạo `QueueEntry` (`QueueType = "DIAGNOSTIC"`, `Status = "WAITING"`, `ManualPriority` chuẩn hóa từ lượt khám lâm sàng theo ràng buộc `ck_queue_entries_manual_priority` `'NORMAL' | 'ELDERLY' | 'PREGNANT'`, cấp số thứ tự tăng dần theo ngày và điểm phục vụ cận lâm sàng, mã phiếu `C001`...) và `DiagnosticOrder` (`OrderCode = "DIA-yyyyMM-NNNN"`, `Status = "ORDERED"`, `Priority = "NORMAL" | "URGENT" | "STAT"`, `RequiredForConclusion`).
   - Mặc định giữ phiên khám ở `IN_PROGRESS` để bác sĩ tiếp tục thao tác trong cùng phiên; nếu `TransitionToAwaitingResults == true`, chuyển `Visit.Status = "AWAITING_RESULTS"` và đóng lượt xếp hàng khám ban đầu (`COMPLETED`) để bệnh nhân thực hiện CLS trước khi quay lại khám (`RETURN_CONSULTATION` — UC28).
   - Ghi `AuditLog` với `Action = "DIAGNOSTIC_ORDER_CREATED"`, `EntityType = "DIAGNOSTIC_ORDER"`, lưu đầy đủ metadata (`orderCode`, `serviceCode`, `category`, `unitPrice`, `priority`, `requiredForConclusion`, `diagnosticQueueEntryId`, `diagnosticTicketNo`, `stateVersion`).

## Consequences

- **Duoc**:
  - Khép kín luồng chỉ định cận lâm sàng từ bàn khám Bác sĩ (`UC34`) sang hàng đợi cận lâm sàng (`UC39`) và luồng quay lại phòng khám sau khi có kết quả (`UC28` / ADR 0019) trên schema `v4.1` hiện có mà không cần migration SQL mới.
  - Phản hồi tức thì `ConsultationWorkspaceResponseDto` kèm `CreatedDiagnosticOrders` và `ClinicalContext.DiagnosticOrders`, đồng thời tự động khóa các hành động kết luận/kê đơn/hoàn tất ca khám khi còn chỉ định bắt buộc (`RequiredForConclusion = true`) chưa có kết quả.
- **Mat**:
  - Mỗi mục dịch vụ cận lâm sàng trong một lần chỉ định tạo 1 bản ghi `queue_entries` (`DIAGNOSTIC`) và 1 bản ghi `diagnostic_orders` riêng biệt để từng phòng/điểm thực hiện cận lâm sàng có thể gọi số và trả kết quả độc lập.
- **Rui ro con lai**:
  - Khi nhân viên cận lâm sàng cập nhật thực hiện và trả kết quả (`UC40`/`UC41`), trạng thái `diagnostic_orders` và `queue_entries` (`DIAGNOSTIC`) sẽ chuyển sang `COMPLETED`, giải phóng khóa `AllMandatoryResultsReady` cho Bác sĩ.
