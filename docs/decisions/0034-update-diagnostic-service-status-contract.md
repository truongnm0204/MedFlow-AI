# 0034 — Update Diagnostic Service Status API Contract & Execution Lifecycle (`#132` / `M3-024` / `UC42`)

- Ngay: 2026-11-11
- Trang thai: accepted
- Lane: high-risk

## Context

`UC42 — Update Service Execution Status` (`#132` / `M3-024`) yêu cầu nhân viên cận lâm sàng (`DIAGNOSTIC_STAFF`) theo dõi và cập nhật trạng thái thực hiện kỹ thuật của các phiếu chỉ định cận lâm sàng (`diagnostic_orders`) từ lúc đã xác nhận/chờ thực hiện (`CONFIRMED`) sang đang thực hiện (`IN_PROGRESS` — lấy mẫu xét nghiệm, chụp chiếu hình ảnh, thăm dò chức năng) và hoàn tất kỹ thuật (`COMPLETED`), hoặc tạm dừng về hàng đợi (`CONFIRMED` / `READY`) hay hủy thực hiện (`CANCELLED`), bao gồm:
1. Xem danh sách và chi tiết phiếu chỉ định cận lâm sàng theo trạng thái thực hiện kỹ thuật kèm lịch sử chuyển trạng thái (`ExecutionHistory`) và thông tin kỹ thuật (`SpecimenCode`, `SpecimenType`, `EquipmentCode`, `ExecutionNote`).
2. Cập nhật trạng thái thực hiện đơn lẻ hoặc hàng loạt (batch) cho các phiếu chỉ định thuộc cùng lượt khám, đồng bộ nguyên tử với lượt xếp hàng cận lâm sàng (`queue_entries` với `queue_type = 'DIAGNOSTIC'`).
3. Bảo toàn trạng thái lượt khám lâm sàng (`visits.status`) và giải phóng điều kiện nhập kết quả cận lâm sàng (`UC43`) cũng như khóa kết quả bắt buộc cho Bác sĩ (`UC36` / `UC38`).

## Decision

1. **Endpoints & Route Aliases (`DiagnosticServiceExecutionController`, `[Authorize(Roles = "DIAGNOSTIC_STAFF")]`)**:
   - `GET /api/v1/diagnostic/orders/execution` (aliases: `/api/v1/diagnostic/orders/execution-status`, `/api/v1/diagnostic/orders/service-status`, `/api/v1/diagnostic/execution`, `/api/v1/diagnostic/service-status`, `/api/v1/diagnostic-orders/execution`, `/api/v1/diagnostic-orders/service-status`, `/api/v1/diagnostic/workbench/execution`) — trả về `DiagnosticServiceExecutionListResponseDto`.
   - `GET /api/v1/diagnostic/orders/{idOrCode}/execution` (aliases: `/api/v1/diagnostic/orders/{idOrCode}/execution-status`, `/api/v1/diagnostic/orders/{idOrCode}/service-status`, `/api/v1/diagnostic/execution/{idOrCode}`, `/api/v1/diagnostic-orders/{idOrCode}/execution`, `/api/v1/diagnostic-orders/{idOrCode}/service-status`, `/api/v1/diagnostic/workbench/orders/{idOrCode}/execution`) — trả về `DiagnosticServiceExecutionDetailResponseDto` kèm `ClinicalContext` và `ExecutionHistory`.
   - `PUT|PATCH|POST /api/v1/diagnostic/orders/{idOrCode}/execution-status` (aliases: `.../service-status`, `.../execution`, `.../status`, `/api/v1/diagnostic/execution/{idOrCode}/status`, `/api/v1/diagnostic-orders/{idOrCode}/execution-status`, `/api/v1/diagnostic-orders/{idOrCode}/service-status`, `/api/v1/diagnostic-orders/{idOrCode}/status`, `/api/v1/diagnostic/workbench/orders/{idOrCode}/execution-status`) — cập nhật trạng thái thực hiện dịch vụ cận lâm sàng (`UpdateDiagnosticServiceStatusResponseDto`).
   - Action shortcuts: `POST|PUT|PATCH /api/v1/diagnostic/orders/{idOrCode}/start` (`.../start-execution`, `/api/v1/diagnostic/execution/{idOrCode}/start`, `/api/v1/diagnostic-orders/{idOrCode}/start`) và `POST|PUT|PATCH /api/v1/diagnostic/orders/{idOrCode}/complete` (`.../complete-execution`, `/api/v1/diagnostic/execution/{idOrCode}/complete`, `/api/v1/diagnostic-orders/{idOrCode}/complete`).
   - Batch endpoint: `POST|PUT|PATCH /api/v1/diagnostic/orders/execution-status` (aliases: `/api/v1/diagnostic/orders/service-status`, `/api/v1/diagnostic/execution/status`, `/api/v1/diagnostic-orders/execution-status`, `/api/v1/diagnostic-orders/service-status`).

2. **Two-Layer Authorization & Hospital Isolation**:
   - Layer 1: `[Authorize(Roles = "DIAGNOSTIC_STAFF")]`.
   - Layer 2 (`EnsureDiagnosticStaffAccessAsync`): kiểm tra `users.account_status == ACTIVE`, không bị khóa (`locked_until`), có role `DIAGNOSTIC_STAFF`, hồ sơ `staff` `ACTIVE` gắn `hospital_id` và khoa đang hoạt động; kiểm tra quyền `QUEUE_MANAGE | DIAGNOSTIC_RESULT_WRITE | DIAGNOSTIC_ORDER` khi đọc và `QUEUE_MANAGE | DIAGNOSTIC_RESULT_WRITE` khi cập nhật trạng thái thực hiện.
   - Mọi truy cập vào phiếu chỉ định, điểm phục vụ hoặc phòng thực hiện thuộc bệnh viện khác bị chặn với `403 Forbidden`.

3. **Service Execution State Machine & Queue Synchronization**:
   - `ORDERED` -> từ chối cập nhật thực hiện với `409 Conflict` (bắt buộc xác nhận qua `UC39` trước).
   - `CONFIRMED` -> cho phép chuyển sang `IN_PROGRESS` (bắt đầu thực hiện kỹ thuật: gán `DiagnosticOrder.Status = "IN_PROGRESS"`, `QueueEntry.Status = "IN_PROGRESS"`, `PresenceStatus = "PRESENT"`, `ArrivedAt`, `ReadyAt`, `CalledAt`, `StartedAt = nowLocal`) hoặc `CANCELLED` (với lý do `5..500` ký tự); chặn chuyển thẳng `CONFIRMED -> COMPLETED` (`409 Conflict`) và chặn bắt đầu khi lượt hàng đợi đang `SKIPPED` (`409 Conflict`).
   - `IN_PROGRESS` -> cho phép chuyển sang `COMPLETED` (`DiagnosticOrder.Status = "COMPLETED"`, `QueueEntry.Status = "COMPLETED"`, `CompletedAt = nowLocal`), tạm dừng về `CONFIRMED` / `READY` / `WAITING` (với lý do `5..500` ký tự, xóa `StartedAt`), hoặc `CANCELLED` (với lý do `5..500` ký tự).
   - `COMPLETED` / `CANCELLED` -> trạng thái kết thúc, mọi thao tác cập nhật trả về `409 Conflict`.
   - Bảo toàn nguyên vẹn `Visit.Status` lâm sàng (`IN_PROGRESS` / `AWAITING_RESULTS`).

4. **Concurrency & Audit Trail**:
   - Kiểm soát đồng thời lạc quan qua `ExpectedStateVersion` và `QueueEntry.StateVersion` (`409 Conflict` khi lệch phiên bản).
   - Ghi nhận `audit_logs` (`entity_type = 'DIAGNOSTIC_ORDER'`) với các action `DIAGNOSTIC_SERVICE_EXECUTION_STARTED`, `DIAGNOSTIC_SERVICE_EXECUTION_COMPLETED`, `DIAGNOSTIC_SERVICE_STATUS_UPDATED`, và `DIAGNOSTIC_SERVICE_EXECUTION_CANCELLED` lưu đầy đủ `specimenCode`, `specimenType`, `equipmentCode`, `executionNote`, `previousStatus`, `targetStatus`, `previousQueueStatus`, `targetQueueStatus`, và `stateVersion`.

## Consequences

- **Duoc**: Đáp ứng đầy đủ đặc tả `UC42` (`#132`), kết nối liền mạch giữa `UC40`/`UC41` (tiếp nhận & hàng đợi CLS) và `UC43` (nhập kết quả CLS), đồng bộ tức thì với `DiagnosticWorklistService` (`UC38`) và `ConsultationWorkspaceService`.
- **Mat**: Thêm `DiagnosticServiceExecutionController` và `DiagnosticServiceExecutionService` chuyên biệt cho nghiệp vụ thực hiện kỹ thuật cận lâm sàng.
- **Rui ro con lai**: Không phát sinh migration SQL; toàn bộ metadata kỹ thuật được lưu trữ và tái dựng an toàn từ `audit_logs` và `queue_entries`.
