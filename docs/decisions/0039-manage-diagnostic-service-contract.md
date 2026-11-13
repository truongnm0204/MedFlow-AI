# 0039 — Manage Diagnostic Service API Contract (`#200` / `#202` / `#204` / `UC51`)

- Ngay: 2026-11-13
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#198` đã có `GET /api/v1/admin/diagnostic-services` (ADR 0037): Hospital Admin đọc danh mục `services.service_type = DIAGNOSTIC` trong đúng cơ sở của mình. UC51 còn ba thao tác ghi:

- `#200` tạo dịch vụ cận lâm sàng
- `#202` cập nhật dịch vụ cận lâm sàng
- `#204` ngừng hoạt động dịch vụ cận lâm sàng

Ba thao tác ghi đụng phân quyền, dữ liệu danh mục và `audit_logs`. Không thêm cột hay migration: bảng `services`, `service_prices`, `audit_logs` đã đủ. Giá sau khi tạo thuộc UC52 (`#206` / `#208`), không đi qua `PUT`.

## Decision

1. **Route và phân quyền lớp 1** trên `AdminDiagnosticServicesController`, vẫn `[Authorize(Roles = "HOSPITAL_ADMIN")]`:
   - `GET /api/v1/admin/diagnostic-services/form-options` → `200 DiagnosticServiceFormOptionsDto`
   - `POST /api/v1/admin/diagnostic-services` → `201 DiagnosticServiceAdminItemDto`
   - `PUT /api/v1/admin/diagnostic-services/{id}` → `200 DiagnosticServiceAdminItemDto`
   - `PATCH /api/v1/admin/diagnostic-services/{id}/deactivate` → `200 DiagnosticServiceAdminItemDto`
   - Body rỗng được phép và đi xuống service. Service ném `ValidationException` (`400`).
   - Lỗi domain đi qua `ExceptionMiddleware`: `ValidationException` 400, `ForbiddenException` 403, `NotFoundException` 404, `ConflictException` 409. Controller không `try/catch ArgumentException`.

2. **Phân quyền lớp 2**:
   - Cả bốn method mới gọi `EnsureHospitalAdminAccessAsync` (cùng quy tắc ADR 0037: user active, không khóa, role `HOSPITAL_ADMIN`, staff active có `hospital_id`, cơ sở tồn tại và đang hoạt động).
   - Sau đó actor phải có permission `HOSPITAL_CONFIG` qua `UserRoles` → `RolePermissions` → `Permissions`. Thiếu quyền → `403` `"Tài khoản không có quyền cấu hình danh mục bệnh viện (HOSPITAL_CONFIG)."`.
   - `GET` danh sách giữ nguyên: chỉ kiểm tra role, không đòi `HOSPITAL_CONFIG`.

3. **Phạm vi dữ liệu**:
   - Mọi ghi chỉ đụng `services` có `hospital_id` của actor và `UPPER(service_type) = 'DIAGNOSTIC'`.
   - Dịch vụ của cơ sở khác, hoặc không phải `DIAGNOSTIC`, trả `404 NotFoundException("Service", id)`, không `403`.
   - `ServiceCode` không sửa được sau khi tạo. `ConsultationWorkspaceService` tra dịch vụ theo mã khi bác sĩ chỉ định.
   - `PUT` không nhận `Price`. Chỉ `POST` nhận giá khởi tạo, ghi một dòng `service_prices` với `EffectiveFrom = now` (unspecified) và `EffectiveTo = null`.

4. **Validation trước khi truy cập DB** (`ValidationException`):
   - Request tạo/sửa `null` → `"Dữ liệu dịch vụ cận lâm sàng không được để trống."`
   - `ServiceCode` sau trim + upper rỗng → `"Mã dịch vụ là bắt buộc."`; dài hơn 40 → `"Mã dịch vụ tối đa 40 ký tự."`; không khớp `^[A-Z0-9][A-Z0-9_.-]*$` → `"Mã dịch vụ chỉ gồm chữ in hoa, số và các ký tự - _ ."`.
   - `Name` trim rỗng → `"Tên dịch vụ là bắt buộc."`; dài hơn 200 → `"Tên dịch vụ tối đa 200 ký tự."`.
   - `Description` trim rỗng thành `null`; dài hơn 2000 → `"Mô tả dịch vụ tối đa 2000 ký tự."`.
   - `DepartmentId` null hoặc ≤ 0 → `"Khoa thực hiện là bắt buộc."`.
   - `SpecialtyId` ≤ 0 → `"Chuyên khoa không hợp lệ."` (`null` nghĩa là không chọn).
   - `Price` ≤ 0 hoặc > 1.000.000.000 → `"Đơn giá phải lớn hơn 0 và không quá 1.000.000.000 đ."`; hơn 2 chữ số thập phân → `"Đơn giá tối đa 2 chữ số thập phân."`. `null` là không tạo bảng giá.
   - `RequiresResultVerification` null: tạo mặc định `true`, sửa giữ giá trị cũ.
   - `serviceId` ≤ 0 → `"Mã dịch vụ cận lâm sàng không hợp lệ."`.
   - Lý do ngừng hoạt động sau trim ngắn hơn 5 hoặc dài hơn 500, hoặc body null → `"Lý do ngừng hoạt động phải từ 5 đến 500 ký tự."`.

5. **Tham chiếu khoa / chuyên khoa** (sau auth):
   - Khoa phải thuộc đúng cơ sở. Không có → `404 NotFoundException("Department", id)`. `IsActive == false` → `409` `"Khoa thực hiện đang ngừng hoạt động."`, trừ khi sửa và `DepartmentId` không đổi.
   - Chuyên khoa (nếu có) không tồn tại → `404 NotFoundException("Specialty", id)`. `IsActive == false` → `409` `"Chuyên khoa đang ngừng hoạt động."`, cùng ngoại lệ không đổi khi sửa.

6. **Tạo**:
   - Trùng `ServiceCode` trong cùng cơ sở (mọi `service_type`, không phân biệt hoa thường, khớp unique index EF `uq_services_hospital_id_service_code`) → `409` `"Mã dịch vụ '{code}' đã tồn tại trong cơ sở."`.
   - Ghi `services` (`AllowsOnlineBooking = false`, `IsActive = true`, `ServiceType = DIAGNOSTIC`) và `service_prices` (nếu có giá) trong một transaction. `DbUpdateException` không phải concurrency được đổi thành cùng `409` trùng mã.
   - Audit `DIAGNOSTIC_SERVICE_CREATED`, `EntityType = SERVICE`, `EntityId` là id, `Reason = "Quản trị bệnh viện tạo dịch vụ cận lâm sàng"`, `ChangeData` gồm serviceId, serviceCode, name, departmentId, specialtyId, requiresResultVerification, price, ipAddress.

7. **Sửa**:
   - Dịch vụ `IsActive == false` → `409` `"Không thể cập nhật dịch vụ cận lâm sàng đang ngừng hoạt động."`.
   - Ghi đè name, description, department, specialty, requiresResultVerification. Không có cột version: last-write-wins.
   - Audit `DIAGNOSTIC_SERVICE_UPDATED`, `Reason = "Quản trị bệnh viện cập nhật dịch vụ cận lâm sàng"`, `ChangeData.before` / `ChangeData.after`.

8. **Ngừng hoạt động**:
   - Đã inactive → `409` `"Dịch vụ cận lâm sàng đã ngừng hoạt động trước đó."`.
   - Còn phiếu `diagnostic_orders` chưa xong → `409` `"Không thể ngừng hoạt động: còn {n} phiếu chỉ định cận lâm sàng chưa hoàn tất sử dụng dịch vụ này."`. Trạng thái đã xong: `COMPLETED`, `DONE`, `FINALIZED`, `VERIFIED` (cùng `ConsultationWorkspaceService`) cộng `CANCELLED`. `Status` null tính là còn mở.
   - Không tắt `queue_service_points`. Bác sĩ không chỉ định được dịch vụ đã tắt vì catalog lọc `IsActive != false` và lúc tạo chỉ định đã có `409` khi dịch vụ inactive.
   - Không có endpoint kích hoạt lại.
   - Audit `DIAGNOSTIC_SERVICE_DEACTIVATED`, `Reason` là lý do đã trim, `ChangeData` gồm previousStatus `ACTIVE`, status `INACTIVE`, activeServicePointCount, ipAddress.

9. **Form options**: khoa của đúng cơ sở, `IsActive != false`, sắp theo tên; chuyên khoa `IsActive != false`, sắp theo tên.

## Consequences

- **Duoc**: Hospital Admin có đủ tạo / sửa / ngừng dịch vụ cận lâm sàng, hai lớp quyền, audit đủ ba action, giá khởi tạo tách khỏi lần sửa.
- **Mat**: Thêm partial `DiagnosticServiceAdminService.Manage.cs` và bốn action trên controller đã có.
- **Rui ro con lai**: Unique `(hospital_id, service_code)` chỉ có trên EF, không có trong SQL. Query trùng mã là chặn chính; `catch DbUpdateException` chỉ bắt race. Hai lần sửa đồng thời: last-write-wins.
