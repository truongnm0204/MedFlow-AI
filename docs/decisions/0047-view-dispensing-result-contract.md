# 0047 — View Dispensing Result Contract (`#150` / `M3-042` / UC46)

- Ngay: 2026-11-14
- Trang thai: accepted
- Lane: high-risk

## Context

Sau khi quy trình cấp phát thuốc hoàn tất (`UC46` / SCR-PHA-03), Dược sĩ cần xem biên nhận cấp phát thuốc (`SCR-PHA-04` / `#149` / `#150` / `M3-042`), bao gồm thông tin chi tiết về đơn thuốc, bệnh nhân, người nhận thuốc, bác sĩ kê đơn, dược sĩ bàn giao, lô FEFO đã xuất, thông tin thanh toán viện phí, và xác thực lưu trữ HIS.

Frontend `#149` đã xây dựng màn hình `DispensingResultPage` với kiểu `DispensingResultDetail` và `DispensedLineItem`. Backend cần cung cấp API chuẩn xác, tuân thủ kiến trúc phân quyền 2 lớp, cô lập dữ liệu theo bệnh viện và ghi nhận nhật ký kiểm toán (audit log).

## Decision

1. **API Endpoints**:
   - `GET /api/v1/pharmacy/dispenses/{idOrCode}`
   - `GET /api/v1/pharmacy/dispensing-results/{idOrCode}`
   - Phân giải `idOrCode`:
     - Nếu là số nguyên: ưu tiên `MedicationDispense.Id`, sau đó mới tìm bản cấp phát mới nhất theo `Prescription.Id`.
     - Nếu có tiền tố `DISP-`: hậu tố phải phân giải được ID và toàn bộ mã phải khớp chính xác mã sinh từ bản ghi.
     - Nếu là mã đơn thuốc: tìm bản ghi cấp phát hoàn tất (`COMPLETED` / `DISPENSED`) tương ứng.

2. **Bảo mật 2 lớp (`401 Unauthorized` / `403 Forbidden` / `404 Not Found`)**:
   - **Layer 1**: `[Authorize(Roles = "PHARMACY_STAFF")]` trên Controller.
   - **Layer 2** (trong Service):
     - Xác thực tài khoản hợp lệ, chưa bị khóa (`LockedUntil <= now`).
     - Xác thực vai trò `PHARMACY_STAFF` trong cơ sở dữ liệu.
     - Xác thực hồ sơ nhân sự `Staff` của Dược sĩ có trạng thái `ACTIVE` và được gán cơ sở y tế `Hospital` đang hoạt động (`IsActive == true`).
     - Kiểm tra quyền hạn `MEDICINE_DISPENSE` nếu hệ thống cấu hình `RolePermissions`.
     - **Cô lập dữ liệu bệnh viện**: Chỉ cho phép xem cấp phát thuộc cơ sở y tế mà dược sĩ trực thuộc (đối chiếu phòng khám, bác sĩ chỉ định, thuốc kê đơn và nhân viên cấp phát). Yêu cầu cấp phát không tồn tại hoặc thuộc cơ sở y tế khác đều trả về `404 Not Found` để chống enumeration.

3. **Kiểm tra dữ liệu đầu vào (`400 BadRequest`)**:
   - `idOrCode` rỗng hoặc toàn khoảng trắng: `ValidationException("Mã hoặc định danh cấp phát thuốc không được để trống.")`.
   - `idOrCode` vượt quá 100 ký tự: `ValidationException("Mã hoặc định danh cấp phát thuốc không được vượt quá 100 ký tự.")`.
   - `idOrCode` là số nguyên âm hoặc 0: `ValidationException("Định danh cấp phát thuốc không hợp lệ.")`.

4. **Dữ liệu trả về (`DispensingResultDetailDto`)**:
   - `dispenseCode`: Mã biên nhận bàn giao chuẩn (`DISP-{yyyy-MMdd}-{Id:D4}`).
   - `rxCode`: Mã đơn thuốc tham chiếu (`PrescriptionCode`).
   - `visitCode`: Mã lượt khám ngoại trú (`VisitCode`).
   - `ticketNo`: Số thứ tự tiếp đón (`TicketNo`).
   - `patientName`, `patientCode`: Thông tin người bệnh.
   - `receiverName`: Thông tin người nhận thuốc (`{patientName} (Chính chủ người bệnh)`).
   - `receiverIdentityMasked`: Mặt nạ định danh CCCD đã xác thực theo chuẩn bảo mật PHI (`****** (Đã xác thực CCCD)`).
   - `prescribingDoctor`: Bác sĩ kê đơn kèm chức danh và phòng khám.
   - `dispensingPharmacist`: Dược sĩ thực hiện bàn giao.
   - `dispensedAt`: Thời điểm bàn giao định dạng `dd/MM/yyyy • HH:mm:ss`.
   - `statusLabel`: Trạng thái xử lý (`DISPENSED & CLOSED`).
   - `paymentSummary`: Tóm tắt khoản thanh toán cấp lượt khám; không suy diễn đây là giá thuốc.
   - `totalAmount`/`totalAmountFormatted` và giá từng dòng: trả `null`/`Chưa có dữ liệu` vì schema hiện tại không lưu giá thuốc hoặc liên kết charge với từng thuốc.
   - `items`: Danh sách các lô thuốc thực tế bàn giao. `shelfLocation` trả `Chưa cập nhật` vì schema hiện tại không lưu vị trí kệ; không tạo dữ liệu giả.
   - `auditNote`: Ghi chú tự động của hệ thống HIS về số dòng/lô đã ghi nhận và trạng thái khóa cấp phát trùng.

5. **Nhật ký kiểm toán (Audit Logging)**:
   - Ghi bản ghi vào bảng `audit_logs` với `Action = "MEDICATION_DISPENSE_VIEW"`, `EntityType = "MEDICATION_DISPENSE"`, `EntityId = dispense.Id`, lưu kèm địa chỉ IP của client và thời gian `OccurredAt`.

## Consequences

- **Tích cực**:
  - Khép kín luồng nghiệp vụ UC46 trên backend, cung cấp hợp đồng dữ liệu tương thích 100% với màn hình FE `DispensingResultPage`.
  - Phân quyền chặt chẽ 2 lớp, bảo vệ nghiêm ngặt thông tin bệnh án và thuốc ngoại trú của bệnh nhân.
  - Hỗ trợ tra cứu linh hoạt bằng cả mã cấp phát (`DISP-...`), mã đơn thuốc (`RX-...`) lẫn ID số nguyên.
- **Chi phí & Ràng buộc**:
  - Mỗi lượt xem thành công tạo 1 bản ghi audit log.
  - Không làm thay đổi hay tạo mới bảng dữ liệu trong cơ sở dữ liệu (tận dụng hoàn toàn bảng `medication_dispenses`, `dispense_items`, `prescriptions`, `prescription_items`, `medicine_batches`).
  - Giá thuốc và vị trí kệ chỉ có thể hiển thị khi schema/catalog bổ sung nguồn dữ liệu có thẩm quyền; API không phân bổ tổng thanh toán lượt khám theo số lượng thuốc.
