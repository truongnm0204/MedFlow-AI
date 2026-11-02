# 0006 — Public Hospital Catalog Contract (`GET /api/v1/hospitals`)

- Ngay: 2026-04-19
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#619` triển khai màn hình Booking Entry Hub (`/booking`, `/appointment/book`) cho phép bệnh nhân (kể cả khách chưa đăng nhập) chọn cơ sở khám ở Bước 1 trước khi chọn hình thức đặt lịch ("Khám theo Bác sĩ" hoặc "Khám theo Dịch vụ") ở Bước 2.

Bảng `hospitals` đã tồn tại trong `database/medflow_schema_v4_1.sql` và có dữ liệu seed `HOS-DEMO-01` tại `database/migrations/003_v41_priority1_seed.sql`, nhưng backend chưa có entity `Hospital`, EF Core configuration, hay endpoint catalog tương ứng.

## Decision

1. **Entity & EF Mapping**: Thêm `MedFlow.Domain.Entities.Hospital` và `HospitalConfiguration` ánh xạ trực tiếp vào bảng `hospitals` hiện có (`id`, `hospital_code`, `name`, `address`, `is_active`). Không tạo SQL migration mới vì DDL bảng đã tồn tại.
2. **Endpoint & Auth**: Cung cấp `GET /api/v1/hospitals` với `[AllowAnonymous]` (tương tự `/api/v1/doctors` và `/api/v1/examination-services`) vì danh sách cơ sở khám là thông tin danh mục công khai, không chứa dữ liệu sở hữu cá nhân (PHI/PII).
3. **Response Shape**: Trả trực tiếp mảng `IReadOnlyList<HospitalDto>` (`id`, `hospitalCode`, `name`, `address`, `isActive`) không bọc phân trang vì số lượng cơ sở trong hệ thống nhỏ.
4. **Ordering & Null Handling**: Sắp xếp ưu tiên cơ sở đang hoạt động (`is_active ?? true` giảm dần), sau đó theo `name` tăng dần và `id` tăng dần. Giá trị `is_active = null` trong DB được quy đổi an toàn thành `true` theo quy ước của `DepartmentService`.

## Consequences

- **Duoc**: Frontend `/booking` lấy được danh sách cơ sở thật từ backend, đồng bộ `hospitalId` vào `useBookingStore` và query string (`/doctors?hospitalId=...`, `/services?hospitalId=...`) để sẵn sàng mở rộng đa cơ sở.
- **Mat**: Thêm `DbSet<Hospital> Hospitals` vào `IApplicationDbContext`, đòi hỏi cập nhật các test double `DbContext` trong `MedFlow.UnitTests`.
- **Rui ro con lai**: Hiện tại các endpoint tìm kiếm bác sĩ và dịch vụ chưa lọc theo `hospitalId` (DB dev/test mới có 1 cơ sở active); khi mở rộng nhiều cơ sở thực tế sẽ bổ sung bộ lọc `hospitalId` cho catalog bác sĩ/dịch vụ.
