# 0021 — Hợp đồng API cập nhật vai trò hệ thống (`PUT/PATCH /api/v1/admin/roles/{roleId}`)

- Ngay: 2026-04-20
- Trang thai: accepted
- Lane: high-risk

## Context

Use case **UC58 — Manage Roles and Permissions** (`#238` / `M2-076`) yêu cầu cung cấp API để quản trị viên hệ thống (`SYSTEM_ADMIN`) cập nhật thông tin cấu hình của một vai trò trong danh mục RBAC (`name`, `description`, `scope`, `status`/`isActive`, `segregationNote`, mã `code` đối với vai trò tùy chỉnh, và danh sách `permissionCodes` tùy chọn).

Trong PostgreSQL schema v4.1 (`database/medflow_schema_v4_1.sql`), bảng `roles` chỉ lưu `id`, `code`, `name`, trong khi các trường siêu dữ liệu quản trị (`scope`, `status`, `description`, `segregationNote`) và lịch sử thay đổi được quản lý kết hợp với bảng `audit_logs` (`entity_type = 'Role'`).

## Decision

1. **Hợp đồng HTTP & Định tuyến (`PUT` và `PATCH`)**:
   - Mở `PUT /api/v1/admin/roles/{roleId}` và `PATCH /api/v1/admin/roles/{roleId}` trên `AdminRolesController` (đồng thời hỗ trợ các route bí danh `/api/v1/system-admin/roles/{roleId}` và `/api/v1/roles/{roleId}`).
   - Tham số `{roleId}` chấp nhận ID số nguyên dương (`"3"`), mã định dạng (`"ROL-03"`), hoặc mã code vai trò (`"DOCTOR"`, `"SECURITY_AUDITOR"`).
   - DTO đầu vào `UpdateRoleRequest` hỗ trợ cập nhật từng phần hoặc toàn bộ (`Code`/`RoleCode`, `Name`/`RoleName`, `Description`, `Scope`, `Status`/`IsActive`, `SegregationNote`, `PermissionCodes`, `Note`/`Reason`) và trả về `RoleListItemDto` (`200 OK`).

2. **Phân quyền 2 lớp (`SYSTEM_ADMIN`)**:
   - **Lớp 1**: `[Authorize(Roles = "SYSTEM_ADMIN")]` tại `AdminRolesController`.
   - **Lớp 2**: `RoleCatalogService.EnsureSystemAdminAccessAsync` xác minh tài khoản người thực hiện tồn tại (`404`), đang ở trạng thái `ACTIVE`, không bị khóa (`LockedUntil`), thực sự được gán vai trò `SYSTEM_ADMIN` trong bảng `user_roles`, và hồ sơ `staff` liên kết (nếu có) đang `ACTIVE` (`403 Forbidden`).

3. **Bảo vệ vai trò hệ thống mặc định, chống tự khóa quyền (Self-lockout) & Phân tách nhiệm vụ (SoD)**:
   - Không cho phép đổi `code` hoặc chuyển `status = INACTIVE` (`isActive = false`) đối với các vai trò hệ thống mặc định (`IsSystemRole == true`: `SYSTEM_ADMIN`, `HOSPITAL_ADMIN`, `DOCTOR`, `RECEPTION`, `DIAGNOSTIC_STAFF`, `PHARMACY_STAFF`, `PATIENT`) → trả `400 Bad Request`.
   - Vai trò `SYSTEM_ADMIN` bắt buộc thuộc phân hệ `SYSTEM` và phải giữ quyền `USER_MANAGE` khi có cập nhật `permissionCodes` → trả `400 Bad Request`.
   - Kiểm tra trùng lặp `code` hoặc `name` (không phân biệt hoa thường) với các vai trò khác → trả `409 Conflict`.
   - Kiểm tra nguyên tắc phân tách nhiệm vụ (SoD): không cho phép gán đồng thời `PRESCRIPTION_ISSUE` và `MEDICINE_DISPENSE` trong cùng một vai trò → trả `400 Bad Request`.

4. **Lưu vết kiểm toán (`ROLE_UPDATED`) & Khôi phục siêu dữ liệu (`ResolveRoleMetadata`)**:
   - Mỗi lần cập nhật vai trò ghi một bản ghi `AuditLog` (`EntityType = "Role"`, `Action = "ROLE_UPDATED"`, `EntityId = role.Id.ToString()`) chứa trạng thái trước và sau khi cập nhật (`roleCode`, `previousRoleCode`, `name`, `previousName`, `scope`, `previousScope`, `status`, `previousStatus`, `description`, `previousDescription`, `segregationNote`, `previousSegregationNote`, `previousPermissions`, `assignedPermissions`, `note`).
   - `RoleCatalogService.LoadCatalogItemsAsync` sử dụng `ResolveRoleMetadata` duyệt các bản ghi `AuditLog` của vai trò theo thứ tự mới nhất trước (`OccurredAt DESC, Id DESC`) để khôi phục `Scope`, `Status`, `Description`, `SegregationNote` mà không bị ghi đè bởi các bản ghi `ROLE_PERMISSIONS_UPDATED` chỉ cập nhật quyền hạn.

## Consequences

- **Duoc**:
  - Đáp ứng đầy đủ nghiệp vụ cập nhật vai trò RBAC (`UC58`) cho cả vai trò hệ thống (chỉnh sửa tên hiển thị, mô tả, ghi chú SoD, quyền hạn hợp lệ) và vai trò tùy chỉnh (cho phép đổi mã `code`, kích hoạt/ngừng hoạt động `ACTIVE`/`INACTIVE`) mà không cần thay đổi schema SQL v4.1.
  - Tương thích hoàn toàn với cấu trúc siêu dữ liệu kiểm toán của tính năng Tạo vai trò (`#236`) và Phân quyền vai trò (`#240`).
- **Mat**:
  - Cần giải mã JSON `ChangeData` trên các bản ghi `AuditLog` của thực thể `Role` khi nạp danh mục vai trò.
- **Rui ro con lai**:
  - Số lượng vai trò trong hệ thống bệnh viện nhỏ (~10–30 vai trò), chi phí đọc `AuditLogs` theo `EntityType == "Role"` không đáng kể; nếu số lượng bản ghi kiểm toán tăng lớn trong tương lai có thể bổ sung cột trực tiếp vào bảng `roles` qua migration mới.
