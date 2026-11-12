# 0037 — View Diagnostic Service List API Contract (`#198` / `M3-058` / `UC51`)

- Ngay: 2026-11-12
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#198` (`M3-058`, UC51 Manage Diagnostic Service) yêu cầu API đọc danh mục dịch vụ cận lâm sàng (`services.service_type = 'DIAGNOSTIC'`) cho **Hospital Admin**.

Trước thay đổi này, `GET /api/v1/admin/services` (`AdminExaminationServicesController`) cho phép lọc `category=diagnostic`, nhưng không giới hạn theo cơ sở y tế của người gọi, dùng `ArgumentException` kết hợp `try/catch` trong controller và trả `Price = 0` khi chưa có bảng giá. `HospitalAdminDashboardService` đã chuẩn hóa kiểm tra quyền hai lớp (`HOSPITAL_ADMIN` + `staff.hospital_id` + cơ sở đang hoạt động) và quy ước `ValidationException` / `ForbiddenException` / `NotFoundException` qua `ExceptionMiddleware`.

## Decision

1. **Route và phân quyền lớp 1**:
   - Endpoint mới: `GET /api/v1/admin/diagnostic-services` trên `AdminDiagnosticServicesController`.
   - `[Authorize(Roles = "HOSPITAL_ADMIN")]` chỉ cho phép `HOSPITAL_ADMIN`. `SYSTEM_ADMIN` nhận `403` vì không gắn với một cơ sở y tế cụ thể (`staff.hospital_id`), đồng nhất với `AdminHospitalDashboardController`.

2. **Phân quyền lớp 2 (Service-level ownership & lifecycle)**:
   - Xác thực tuần tự từ DB theo `actorUserId`:
     - `Users` tồn tại (`404 NotFoundException("User", actorUserId)` nếu thiếu).
     - `AccountStatus == ACTIVE` (`403 ForbiddenException` nếu không hoạt động).
     - `LockedUntil` không còn hiệu lực (`403 ForbiddenException` nếu đang bị tạm khóa).
     - Có role `HOSPITAL_ADMIN` trong DB (`UserRoles` + `Roles`, `403 ForbiddenException` nếu thiếu).
     - Có hồ sơ `Staff` gắn `HospitalId != null` và `Staff.Status == "ACTIVE"` (`403 ForbiddenException` nếu thiếu hoặc ngừng hoạt động).
     - Cơ sở `Hospitals` tồn tại (`404 NotFoundException("Hospital", hospitalId)`) và `IsActive != false` (`403 ForbiddenException` nếu ngừng hoạt động).

3. **Phạm vi dữ liệu (Hospital Scoping)**:
   - Chỉ truy vấn `services` có `hospital_id = staff.hospital_id` và `UPPER(service_type) = 'DIAGNOSTIC'`.
   - Không nhận `HospitalId` từ query request; các dịch vụ có `hospital_id IS NULL` hoặc thuộc bệnh viện khác bị loại hoàn toàn.
   - Điểm phục vụ cận lâm sàng lấy từ `queue_service_points` có `UPPER(point_type) = 'DIAGNOSTIC'`, `service_id` thuộc tập dịch vụ của cơ sở, và `hospital_id = staff.hospital_id OR hospital_id IS NULL`.

4. **Quy tắc kiểm tra đầu vào (Validation trước khi truy cập DB)**:
   - Mọi lỗi đầu vào ném `MedFlow.Domain.Exceptions.ValidationException` (`400 Bad Request`, title `"Lỗi xác thực"`):
     - `Page < 1` → `"Trang yêu cầu phải lớn hơn hoặc bằng 1."`
     - `PageSize < 1 || PageSize > 100` → `"Số lượng bản ghi trên một trang phải từ 1 đến 100."`
     - `Keyword` sau `Trim()` dài hơn 100 ký tự → `"Từ khóa tìm kiếm tối đa 100 ký tự."`
     - `DepartmentId <= 0` → `"Mã khoa phòng lọc không hợp lệ."`
     - `SpecialtyId <= 0` → `"Mã chuyên khoa lọc không hợp lệ."`
     - `Status` khác `null`/rỗng/`ALL`/`ACTIVE`/`INACTIVE` (không phân biệt hoa thường) → `"Trạng thái dịch vụ cận lâm sàng lọc không hợp lệ."`

5. **Metrics, giá dịch vụ và hành vi đọc**:
   - `Metrics` (`Total`, `Active`, `Inactive`, `RequiresVerificationCount`, `DepartmentsCount`, `ActiveServicePointsCount`) được tính trên toàn bộ dịch vụ cận lâm sàng của cơ sở **trước** khi áp bộ lọc truy vấn.
   - Khi dịch vụ không có dòng `service_prices`, trả `Price = null` và `PriceFormatted = null` (không ép về `0`).
   - Endpoint chỉ đọc (`GET`): không gọi `SaveChangesAsync`, không ghi `audit_logs`.

## Consequences

- **Duoc**: Hospital Admin có API riêng, an toàn hai lớp, cô lập đúng theo bệnh viện, trả đầy đủ thông tin khoa/chuyên khoa/giá/điểm phục vụ và thống kê tổng quan.
- **Mat**: Thêm một cặp controller/service mới trong `MedFlow.API` và `MedFlow.Application.Services.Services`.
- **Rui ro con lai**: Endpoint cũ `GET /api/v1/admin/services?category=diagnostic` vẫn giữ nguyên để tránh vỡ tương thích và chưa giới hạn theo `hospital_id`.
