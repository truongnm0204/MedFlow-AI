# 0046 — Hợp đồng API danh sách tài khoản nhân sự

- Ngày: 2026-11-16
- Trạng thái: accepted
- Lane: high-risk

## Context

UC57 (`#226` / `M1-088`) cần danh sách tài khoản nhân sự và trạng thái đăng nhập. Giao diện `/admin/staff` cho phép `SYSTEM_ADMIN` quản lý toàn hệ thống và cho `HOSPITAL_ADMIN` xem ở chế độ chỉ đọc. Bảng `staff` có phạm vi bệnh viện; trạng thái đăng nhập thực tế nằm trong `users`.

## Decision

1. Cung cấp `GET /api/v1/admin/staff` và bí danh `GET /api/v1/system-admin/staff`, với tìm kiếm, bộ lọc bệnh viện/khoa/vai trò/trạng thái và phân trang.
2. Lớp 1 cho phép JWT role `SYSTEM_ADMIN,HOSPITAL_ADMIN`. Lớp 2 xác minh user, khóa tạm thời, vai trò trong `user_roles`, và hồ sơ staff đang hoạt động.
3. `SYSTEM_ADMIN` xem toàn hệ thống; `HOSPITAL_ADMIN` chỉ xem bệnh viện gắn với hồ sơ staff. Bộ lọc bệnh viện khác phạm vi trả `403`.
4. Chỉ trả staff có user liên kết. Trạng thái hiệu lực ưu tiên khóa (`LOCKED`), ánh xạ `users.status = INACTIVE` hoặc hồ sơ staff ngừng hoạt động thành `DISABLED`, giữ `PENDING`, còn lại là `ACTIVE`.
5. Metrics được tính trong cùng phạm vi quyền trước bộ lọc danh sách, để các thẻ tổng quan không thay đổi khi tìm kiếm/phân trang.

## Consequences

- Được: hợp đồng khớp màn hình hiện tại, chống JWT role giả và chống đọc chéo bệnh viện, không cần đổi schema.
- Mất: trạng thái `INACTIVE` của domain được trình bày là `DISABLED` trên contract staff để tương thích UI.
- Rủi ro còn lại: staff có nhiều vai trò cần một vai trò chính để hiển thị; API trả toàn bộ vai trò và chọn vai trò chính theo thứ tự role ID ổn định.
