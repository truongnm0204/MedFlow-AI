# 0046 — Confirm Medicine Dispensing Contract (`UC46` / Issue `#148` / `M3-040`)

- Ngay: 2026-11-14
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#148` yeu cau API va business rules cho Dược sĩ xác nhận bàn giao thuốc (UC46, SCR-PHA-03) và phát hành biên nhận cấp phát (SCR-PHA-04).
Sau khi bác sĩ kê đơn và hoàn tất khám (`UC38`), đơn thuốc có trạng thái `ISSUED` với phương thức cấp phát tại viện (`HOSPITAL` / `HOSPITAL_PHARMACY`).
Dược sĩ cần:
1. Xem thông tin người nhận, đơn thuốc, các khoản thuốc cần bàn giao kèm hướng dẫn liều dùng, cảnh báo dược lâm sàng, thông tin thanh toán và checklist 3 mục kiểm tra GPP (`SCR-PHA-03`).
2. Xác nhận bàn giao thực tế: kiểm tra danh tính người nhận, hoàn tất checklist, kiểm tra tính hợp lệ của lô thuốc (FEFO, còn hạn, không bị khóa, đủ tồn kho), trừ tồn kho thực tế của các lô thuốc, ghi nhận giao dịch kho `StockTransaction`, cập nhật `MedicationDispense` và `Prescription` sang `DISPENSED`, ghi nhận vết kiểm toán `AuditLog`.
3. Trả về hoặc xem lại biên nhận cấp phát hoàn chỉnh (`SCR-PHA-04`).

## Decision

1. **HTTP Endpoints**:
   - `GET /api/v1/pharmacy/prescriptions/{idOrCode}/dispense` (canonical): Lấy ngữ cảnh bàn giao đơn thuốc (`DispenseConfirmContextDto`).
     - Alias: `GET /api/v1/pharmacy/dispense/{idOrCode}`
   - `POST /api/v1/pharmacy/prescriptions/{idOrCode}/dispense` (canonical): Xác nhận bàn giao thuốc và trừ tồn kho (`DispensingResultDetailDto`).
     - Alias: `POST /api/v1/pharmacy/dispenses/confirm`, `POST /api/v1/pharmacy/prescriptions/{idOrCode}/confirm-dispense`
   - `GET /api/v1/pharmacy/prescriptions/{idOrCode}/dispense-result` (canonical): Xem biên nhận cấp phát thuốc (`DispensingResultDetailDto`).
     - Alias: `GET /api/v1/pharmacy/dispenses/{idOrCode}`

2. **Two-Layer Authorization**:
   - **Layer 1**: `[Authorize(Roles = "PHARMACY_STAFF")]`.
   - **Layer 2** (trong Service):
     - `actorUserId > 0`, tài khoản tồn tại, `AccountStatus == ACTIVE`, `LockedUntil` không bị khóa.
     - Role người dùng có `PHARMACY_STAFF`.
     - Nhân sự `Staff` của dược sĩ đang hoạt động (`ACTIVE`) và được gán cơ sở y tế `HospitalId`.
     - Bệnh viện `Hospital` đang hoạt động (`IsActive == true`).
     - Quyền `MEDICINE_DISPENSE` được kiểm tra khi hệ thống đã cấu hình phân quyền vai trò.

3. **Hospital Isolation & Prescription Scope**:
   - Đơn thuốc phải tồn tại và gắn với lượt khám `Visit` hợp lệ thuộc cùng bệnh viện với dược sĩ.
   - Bác sĩ kê đơn và toàn bộ thuốc kê phải thuộc cùng bệnh viện của dược sĩ.
   - Phương thức cấp phát phải là `HOSPITAL` hoặc `HOSPITAL_PHARMACY` (không xử lý đơn mua ngoài viện).
   - Đơn thuốc không được ở trạng thái `CANCELLED`, `DRAFT`, hoặc bị thay thế bởi đơn thuốc khác (`replaces_prescription_id`).
   - Nếu đơn thuốc đã ở trạng thái `DISPENSED`, từ chối cấp phát lại (trả về `409 Conflict`), trừ trường hợp gọi lại với cùng `idempotencyKey` thì trả về kết quả thành công trước đó (idempotency).

4. **Payment Guard**:
   - Đối chiếu trạng thái thanh toán của lượt khám (`Payment` / `VisitCharge`): Nếu có phát sinh khoản thu nhưng chưa thanh toán (`PENDING` và không có giao dịch `PAID`), từ chối xuất kho với lỗi 409/400 ("Chặn xuất kho: Chưa hoàn tất thanh toán").

5. **Receiver & Checklist Validation**:
   - `isIdentityVerified` bắt buộc là `true`. Nếu `false` → trả về `400 ValidationException`.
   - `checksConfirmed` bắt buộc là `true` (hoặc xác nhận đủ 3 mục checklist GPP của dược sĩ).

6. **Batch Allocation, Stock Deduction & Data Integrity**:
   - Mỗi khoản thuốc được cấp phát theo lô đã chuẩn bị (hoặc chỉ định trong request, hoặc tự động theo chuẩn FEFO nếu chưa chuẩn bị).
   - Kiểm tra lô thuốc:
     - Lô thuốc phải thuộc đúng mặt hàng thuốc kê.
     - Lô không bị khóa (`is_blocked != true`).
     - Lô còn hạn sử dụng (`expiry_date > today`).
     - Tồn kho khả dụng của lô phải đủ cho số lượng xuất (`quantity_on_hand >= dispensed_quantity`).
   - Cập nhật kho và trạng thái trong transaction:
     - Trừ tồn kho: `quantity_on_hand = quantity_on_hand - dispensed_quantity`.
     - Tạo bản ghi `StockTransaction` với `transaction_type = "DISPENSE"`, `quantity_delta = -dispensed_quantity`, `source_reference = prescription_code`.
     - Cập nhật / tạo `MedicationDispense` với `status = "DISPENSED"`, `dispensed_at = nowUtc`, `dispensed_by_staff_id = staff.Id`.
     - Cập nhật / tạo `DispenseItem` với `dispensed_quantity`.
     - Cập nhật `Prescription.status = "DISPENSED"`.

7. **Audit Logging & PHI Protection**:
   - Ghi nhật ký kiểm toán `AuditLog` với `action = "MEDICINE_DISPENSED"`, `entity_type = "Prescription"`, `entity_id = prescription.Id.ToString()`.
   - Lưu IP máy trạm (từ `X-Forwarded-For` hoặc connection IP).
   - Không để lộ số CCCD đầy đủ trong biên nhận công khai (mask dạng `0010****8892`).

## Consequences

- **Được**:
  - Khép kín luồng cấp phát thuốc ngoại trú UC46, đồng bộ với SCR-PHA-03 và SCR-PHA-04 trên frontend.
  - Đảm bảo an toàn dược lâm sàng (FEFO, chặn lô hết hạn/khóa, chặn xuất kho khi chưa thanh toán).
  - Đảm bảo toàn vẹn dữ liệu tồn kho HIS và lưu vết kiểm toán đầy đủ.
- **Mất**:
  - Cần thực hiện transaction đồng thời trên nhiều bảng (`prescriptions`, `medication_dispenses`, `dispense_items`, `medicine_batches`, `stock_transactions`, `audit_logs`).
