# 0038 — Prepare Medicines According to Prescription Contract (UC45 / #144)

- Ngay: 2026-11-12
- Trang thai: accepted
- Lane: high-risk

## Context

Nhân viên nhà thuốc (`PHARMACY_STAFF`) sau khi tra cứu đơn thuốc (`UC44` / `#142`) cần thực hiện soạn thuốc theo đơn (`UC45` / `#144` / `SCR-PHA-02`), đối chiếu thông tin lâm sàng (bác sĩ kê, phòng khám, chẩn đoán ICD-10, tiền sử dị ứng nguy hiểm, hướng dẫn dùng thuốc) và lựa chọn lô thuốc theo nguyên tắc **FEFO (First Expired, First Out)** từ kho dược của bệnh viện trước khi chuyển sang bước phát thuốc (`UC46`).

Frontend màn hình Soạn thuốc (`SCR-PHA-02`) đã hoàn thiện và tích hợp vào `develop` qua PR #733 (`[M3][FE] Prepare Medicines (#143)`), hiện đang dùng dữ liệu mock (`PREPARE_MEDICINE_CONTEXT_MOCK`) và cần Backend API thực tế.

## Decision

1. **Endpoints & Routes (`PharmacyPrepareMedicinesController`, `[Authorize(Roles = "PHARMACY_STAFF")]`)**:
   - `GET /api/v1/pharmacy/prescriptions/{idOrCode}/prepare` (Route alias: `GET /api/v1/pharmacy/prepare/{idOrCode}`) — Trả về `PreparePrescriptionContextDto` gồm đầy đủ ngữ cảnh lâm sàng của đơn thuốc, thông tin từng khoản thuốc kê, và danh sách các lô thuốc khả dụng được sắp xếp ưu tiên theo thuật toán FEFO.
   - `POST /api/v1/pharmacy/prescriptions/{idOrCode}/prepare` (Route alias: `POST /api/v1/pharmacy/prepare/{idOrCode}`) — Nhận `PreparePrescriptionRequestDto`, xác nhận soạn thuốc, tạo hoặc cập nhật bản ghi `medication_dispenses` và `dispense_items`, chuyển trạng thái đơn thuốc sang `READY` (sẵn sàng phát thuốc).

2. **Two-Layer Authorization & Hospital Scoping**:
   - Layer 1: `[Authorize(Roles = "PHARMACY_STAFF")]`.
   - Layer 2: Kiểm tra database `users.account_status == ACTIVE`, tài khoản không bị khóa (`locked_until`), nhân viên có hồ sơ `staff` `ACTIVE` gắn `hospital_id`, cơ sở y tế `ACTIVE`, và có quyền `MEDICINE_DISPENSE`.
   - Cách ly dữ liệu: Đơn thuốc, lượt khám, bác sĩ kê, danh mục thuốc và các lô thuốc phải thuộc cùng bệnh viện của dược sĩ (`hospital_id`). Đơn thuốc thuộc cơ sở khác hoặc không hợp lệ trả về `404 Not Found`.

3. **Thuật toán FEFO (First Expired, First Out) & Kiểm soát Lô**:
   - Tự động lọc các lô thuốc (`medicine_batches`) thuộc từng loại thuốc trong bệnh viện:
     - Bỏ qua lô đã hết hạn (`expiry_date <= todayUtc`).
     - Bỏ qua lô bị tạm khóa (`is_blocked == true`).
   - Sắp xếp tăng dần theo hạn sử dụng: `OrderBy(b => b.ExpiryDate).ThenBy(b => b.Id)`.
   - Lô hợp lệ đầu tiên còn tồn kho khả dụng (`quantity_on_hand > 0`) được tự động đánh dấu cờ `IsFefoRecommended = true`.
   - Trả về 4 tiêu chuẩn an toàn dược lâm sàng & kiểm soát GPP tương thích với checklist màn hình FE.

4. **Validation & State Transitions**:
   - Đơn thuốc phải ở trạng thái `ISSUED`, `PREPARING` hoặc `READY`. Chặn các đơn đã hủy, đơn dự thảo, đơn bị thay thế, hoặc đơn đã hoàn tất phát thuốc (`DISPENSED` -> `409 Conflict`).
   - Mọi khoản thuốc trong đơn phải được soạn đủ số lượng (`pickedQuantity == prescribedQuantity`), trừ khi có lý do thiếu thuốc hợp lệ (`shortageReason`).
   - Lô thuốc được chọn phải thuộc đúng loại thuốc đã kê, còn hạn dùng, không bị khóa, và tồn kho khả dụng tại thời điểm soạn phải đủ đáp ứng (`quantity_on_hand >= pickedQuantity` -> nếu không đủ trả về `400 BadRequest` / `409 Conflict`).
   - Trạng thái: Cập nhật hoặc tạo mới `medication_dispenses` với `status = "READY"`, `prepared_by_staff_id = actorStaffId`, `prepared_at = nowUtc`. Ghi nhận `dispense_items` với `planned_quantity = pickedQuantity`, `dispensed_quantity = 0` (chỉ trừ tồn kho thực tế khi xác nhận cấp phát tại UC46). Đồng bộ `prescription.status = "READY"`.

5. **Audit Logging**:
   - Mọi thao tác xem chuẩn bị thuốc và xác nhận soạn thuốc được ghi vết vào `audit_logs` (`entity_type = 'PRESCRIPTION'`) với action `PRESCRIPTION_PREPARE_VIEW` và `PRESCRIPTION_PREPARED`.

## Consequences

- **Được**: Đáp ứng hoàn hảo UC45 (#144), khớp 100% với Frontend contract (#143 / SCR-PHA-02), chuẩn hóa thuật toán FEFO, kiểm soát an toàn dược, cách ly đa bệnh viện, không yêu cầu DB migration.
- **Mất**: Thêm controller, service, DTOs và bộ test chuyên biệt cho phân hệ soạn thuốc.
- **Rủi ro còn lại**: Thao tác trừ tồn kho và ghi nhận thẻ kho (`stock_transactions`) thuộc phạm vi UC46 (Xác nhận cấp phát thuốc), UC45 chỉ ghi nhận `planned_quantity` trên `dispense_items`.
