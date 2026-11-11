# 0032 — Manage Diagnostic Queue API Contract & Diagnostic Queue Lifecycle (`#130` / `M3-022` / `UC41`)

- Ngay: 2026-11-11
- Trang thai: accepted
- Lane: high-risk

## Context

`UC41 — Manage Diagnostic Queue` (`#130` / `M3-022`) yêu cầu nhân viên cận lâm sàng (`DIAGNOSTIC_STAFF`) xem và điều phối hàng đợi cận lâm sàng (`queue_entries` với `queue_type = 'DIAGNOSTIC'`) theo từng điểm phục vụ (`queue_service_points` với `point_type = 'DIAGNOSTIC'`), bao gồm:
1. Xem bảng hàng đợi cận lâm sàng theo điểm phục vụ/phòng thực hiện với thứ tự phục vụ (`ServingOrder`) tất định.
2. Gọi bệnh nhân đủ điều kiện tiếp theo (`call-next`), cập nhật trạng thái (`WAITING`, `READY`, `CALLED`, `IN_PROGRESS`, `SKIPPED`, `COMPLETED`, `CANCELLED`), đánh dấu qua lượt (`missed-turn`), khôi phục lượt chờ (`restore-turn`), và hủy lượt xếp hàng (`cancel`).
3. Đồng bộ trạng thái phiếu chỉ định cận lâm sàng (`diagnostic_orders.status`) mà **không ghi đè** trạng thái lượt khám lâm sàng (`visits.status` đang ở `IN_PROGRESS` hoặc `AWAITING_RESULTS`).

## Decision

1. **Endpoints & Route Aliases (`DiagnosticQueueController`, `[Authorize(Roles = "DIAGNOSTIC_STAFF")]`)**:
   - `GET /api/v1/diagnostic/queue` (aliases: `/api/v1/diagnostic-queue`, `/api/v1/diagnostic/queues`, `/api/v1/diagnostic/workbench/queue`, `/api/v1/diagnostic/queue-entries`) — trả về `DiagnosticQueueBoardResponseDto`.
   - `GET /api/v1/diagnostic/queue/{idOrCode}` (aliases: `/api/v1/diagnostic/queue/entries/{idOrCode}`, `/api/v1/diagnostic/queue-entries/{idOrCode}`, `/api/v1/diagnostic/queue/{idOrCode}/status`) — trả về `DiagnosticQueueEntryDetailResponseDto` kèm `ClinicalContext` và `StatusHistory` tái dựng từ `audit_logs`.
   - `POST /api/v1/diagnostic/queue/service-points/{servicePointId}/call-next` (aliases: `/api/v1/diagnostic/service-points/{servicePointId}/call-next`, `/api/v1/diagnostic/queue/call-next`) — gọi bệnh nhân đủ điều kiện tiếp theo tại điểm phục vụ (`CallNextDiagnosticPatientResponseDto`).
   - `PUT|PATCH|POST /api/v1/diagnostic/queue/{idOrCode}/status` và các shortcut `POST .../{idOrCode}/ready|call|start|complete` — chuyển trạng thái lượt xếp hàng cận lâm sàng (`ManageDiagnosticQueueActionResponseDto`).
   - `POST /api/v1/diagnostic/queue/{idOrCode}/missed-turn` (alias `/skip`), `POST /api/v1/diagnostic/queue/{idOrCode}/restore-turn` (alias `/restore`), `POST /api/v1/diagnostic/queue/{idOrCode}/cancel`.

2. **Two-Layer Authorization & Hospital Isolation**:
   - Layer 1: `[Authorize(Roles = "DIAGNOSTIC_STAFF")]`.
   - Layer 2 (`EnsureDiagnosticStaffAccessAsync`): kiểm tra `users.account_status == ACTIVE`, không bị khóa (`locked_until`), có role `DIAGNOSTIC_STAFF`, hồ sơ `staff` `ACTIVE` gắn `hospital_id` của bệnh viện và khoa đang hoạt động; kiểm tra quyền `QUEUE_MANAGE | DIAGNOSTIC_RESULT_WRITE | DIAGNOSTIC_ORDER` khi đọc và `QUEUE_MANAGE | DIAGNOSTIC_RESULT_WRITE` khi điều phối.
   - Mọi truy cập sang điểm phục vụ hoặc lượt xếp hàng thuộc bệnh viện khác trả về `403 Forbidden`; truy cập vào `queue_entries` lâm sàng (`queue_type != 'DIAGNOSTIC'`) trả về `409 Conflict`.

3. **Deterministic Bucket Ranking (`ServingOrder`)**:
   - Trong mỗi `DiagnosticServicePointQueueBucketDto`, các lượt hoạt động được xếp hạng 1-based theo:
     1. Nhóm đang phục vụ (`IN_PROGRESS` trước `CALLED`).
     2. Nhóm chờ đủ điều kiện (`READY`/`WAITING` có `IsEligible = true`): ưu tiên chỉ định `STAT > URGENT > NORMAL`, ưu tiên lâm sàng `PREGNANT > ELDERLY > NORMAL`, `RequiredForConclusion = true` trước `false`, `ReadyAt ?? ArrivedAt` FIFO, và `QueueNumber` tăng dần.
     3. Nhóm chưa đủ điều kiện (`ORDERED` chưa xác nhận hoặc `PresenceStatus != PRESENT`) và nhóm kết thúc (`SKIPPED`, `COMPLETED`, `CANCELLED`).

4. **Diagnostic Order Sync & Clinical Visit Status Preservation**:
   - Chuyển sang `IN_PROGRESS` đồng bộ `DiagnosticOrder.Status = "IN_PROGRESS"`; chuyển sang `COMPLETED` đồng bộ `DiagnosticOrder.Status = "COMPLETED"`; hủy lượt với `CancelLinkedOrder = true` đồng bộ `DiagnosticOrder.Status = "CANCELLED"`.
   - Gọi hoặc bắt đầu thực hiện khi `DiagnosticOrder.Status == "ORDERED"` bị chặn với `409 Conflict`.
   - `DiagnosticQueueService` và `QueueStatusService.ApplyStatusTransition` (được bảo vệ bởi `ClinicalQueueRules.IsClinicalExaminationQueueType(queueEntry.QueueType)`) tuyệt đối không ghi đè `Visit.Status` khi thao tác trên hàng đợi `DIAGNOSTIC`.

5. **Concurrency & Audit Trail**:
   - Kiểm soát đồng thời lạc quan qua `ExpectedStateVersion` và `QueueEntry.StateVersion` (`409 Conflict` khi lệch phiên bản).
   - Ghi nhận `audit_logs` (`entity_type = 'QUEUE_ENTRY'`) với các action `DIAGNOSTIC_QUEUE_CALL_NEXT`, `DIAGNOSTIC_QUEUE_STATUS_UPDATED`, `DIAGNOSTIC_QUEUE_MISSED_TURN`, `DIAGNOSTIC_QUEUE_TURN_RESTORED`, và `DIAGNOSTIC_QUEUE_ENTRY_CANCELLED`.

## Consequences

- **Duoc**: Đáp ứng đầy đủ đặc tả `UC41` (`#130`), tích hợp liền mạch với `UC40` (`DiagnosticOrderReviewService`) và `UC39` (`DiagnosticWorklistService`), bảo toàn tính toàn vẹn trạng thái `Visit` lâm sàng và cung cấp lịch sử chuyển trạng thái đầy đủ cho giao diện Diagnostic Workbench.
- **Mat**: Thêm một service chuyên biệt (`DiagnosticQueueService`) tách biệt với `QueueStatusService` của luồng khám lâm sàng.
- **Rui ro con lai**: Không có thay đổi schema DB; tận dụng hoàn toàn bảng `queue_entries`, `queue_service_points`, `diagnostic_orders`, và `audit_logs` hiện hữu.
