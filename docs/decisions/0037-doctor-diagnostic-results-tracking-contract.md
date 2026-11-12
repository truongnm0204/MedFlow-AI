# 0037 — Doctor Diagnostic Results Tracking API Contract (`#122` / `M3-014` / `UC35`)

- Ngay: 2026-11-12
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#122` (`M3-014` / `UC35`) yêu cầu một endpoint đọc tổng hợp cho bác sĩ lâm sàng để theo dõi tiến độ thực hiện và trả kết quả cận lâm sàng theo từng lượt khám trong ngày. Hiện tại `#114` mới có endpoint đọc chi tiết theo từng lượt khám (`GET /api/v1/doctor/consultation/{idOrCode}/diagnostic-results`). Màn hình FE (`#121`, `diagnostic-results-tracking-page.tsx`) là nhánh riêng.

## Decision

1. **Endpoint chỉ dành cho `DOCTOR`** trên `DoctorConsultationController` (`[Authorize(Roles = "DOCTOR")]`):
   - `GET /api/v1/doctor/diagnostic-results/tracking`
   - Nhân sự cận lâm sàng (`DIAGNOSTIC_STAFF`) dùng `#138` (`GET /api/v1/diagnostic/results/view`), không dùng chung endpoint này.

2. **Ownership layer 2 theo bác sĩ.** Lượt khám hợp lệ phải thuộc bác sĩ đang đăng nhập theo điều kiện:
   - `v.AssignedDoctorId == doctorId || (v.AssignedDoctorId == null && v.CompletedByDoctorId == doctorId)`
   - Lượt khám đã chuyển giao cho bác sĩ khác (`AssignedDoctorId != doctorId`) bị loại trừ.

3. **Loại trừ lượt khám và phiếu đã hủy.**
   - Loại bỏ các lượt khám có trạng thái `CANCELLED` hoặc `NO_SHOW` (đồng nhất với `#114` trả `409` cho lượt khám hủy/vắng).
   - Loại bỏ các phiếu chỉ định có trạng thái `CANCELLED` khỏi danh sách và khỏi mẫu số tính phần trăm hoàn thành. Lượt khám không còn phiếu hợp lệ nào sau khi lọc sẽ không xuất hiện.

4. **Ngữ nghĩa lọc theo ngày (`date`).**
   - `date` (`yyyy-MM-dd`, mặc định ngày hiện tại theo giờ Việt Nam) chọn các lượt khám có ít nhất một `diagnostic_orders.ordered_at` nằm trong ngày đó.
   - Khi đã chọn được lượt khám, tiến độ hoàn thành (`TotalOrders`, `SignedOrders`, `CompletionPercent`, `Orders`) tính trên **toàn bộ** phiếu chỉ định không hủy của lượt khám đó (không cắt riêng các phiếu trong ngày).

5. **Ánh xạ trạng thái theo dõi (`TrackingStatus`).**
   - **Cấp phiếu chỉ định (`DiagnosticResultsTrackingOrderDto.TrackingStatus`)** dựa trên `ResolveDiagnosticOrderResultState(order, queueEntry, result, latestVersion)` (thứ tự ưu tiên từ trên xuống):
     - `state.IsResultReady == true` → `SIGNED`
     - `result is not null` (đã có dòng kết quả nháp/chờ duyệt chưa khóa: `DRAFT`, `PRELIMINARY`, `PENDING_VERIFY`) → `AWAITING_SIGN`
     - `orderStatus == "IN_PROGRESS"` hoặc `NormalizeQueueStatus(queueEntry.Status)` thuộc `CALLED` / `IN_PROGRESS` → `IN_PROGRESS`
     - Còn lại → `PENDING`
   - **Cấp lượt khám (`DiagnosticResultsTrackingVisitDto.TrackingStatus`)**:
     - Tất cả phiếu đều `SIGNED` → `COMPLETED`
     - Không còn phiếu `PENDING` hoặc `IN_PROGRESS` (tức chỉ còn `SIGNED` + `AWAITING_SIGN`) → `PENDING_SIGN`
     - Còn lại → `IN_PROGRESS`
   - **Thống kê tổng (`Stats`)** tính trên toàn bộ lượt khám hợp lệ của bác sĩ trong ngày trước khi áp bộ lọc `status`, `keyword` và phân trang (`page`, `pageSize`).

6. **Không có cờ bất thường (`abnormal`).**
   - Schema `diagnostic_results` và `diagnostic_result_versions` không có cột cờ bất thường. Contract không trả trường abnormal; `#121` (FE) bỏ thẻ thống kê và bộ lọc bất thường.

7. **Chỉ đọc, không tạo tác dụng phụ.**
   - Không gọi `SaveChanges`, không ghi `audit_logs`, không tăng `state_version` (đồng nhất chính sách đọc với `#114` và ADR `0036`).

## Consequences

- **Duoc**: Bác sĩ theo dõi tập trung tiến độ CLS của toàn bộ lượt khám mình phụ trách trong ngày với quy tắc hoàn thành đồng nhất `#114`.
- **Mat**: Thêm một DTO contract và một partial file trên `ConsultationWorkspaceService`.
- **Rui ro con lai**: Nếu lượt khám kéo dài nhiều ngày, việc lọc ứng viên theo `OrderedAt` trong ngày chỉ lấy các lượt khám có phát sinh chỉ định vào ngày được chọn.
