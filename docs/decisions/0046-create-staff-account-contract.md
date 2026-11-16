# 0046 — Hợp đồng tạo tài khoản nhân sự

- Ngay: 2026-11-16
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#228` / `M1-090` / `UC57` yêu cầu System Admin tạo tài khoản nhân sự với xác thực đầu vào, RBAC, hồ sơ cơ sở/khoa, tính toàn vẹn dữ liệu và lỗi nhất quán. Schema hiện có đã cung cấp `users`, `staff`, `roles`, `user_roles`, `hospitals`, `departments` và `audit_logs`; không cần migration.

Form frontend hiện dùng mã/tên catalog (`facility`, `department`) và các mã vai trò giao diện `DOCTOR`, `TECHNICIAN`, `NURSE`, `RECEPTIONIST`, trong khi RBAC seed dùng `DOCTOR`, `DIAGNOSTIC_STAFF`, `RECEPTION`.

## Decision

1. Mở `POST /api/v1/admin/staff-accounts` và alias `/api/v1/system-admin/staff-accounts`; response `201 Created` chỉ chứa định danh và dữ liệu hồ sơ, không chứa password/hash.
2. Phân quyền hai lớp:
   - Controller yêu cầu claim `SYSTEM_ADMIN`.
   - Service tải actor từ DB, yêu cầu user `ACTIVE`, không bị khóa, có assignment `SYSTEM_ADMIN`, và staff profile của actor (nếu có) phải `ACTIVE`.
3. Resolve `facility` và `department` theo code hoặc tên không phân biệt hoa thường. Cả hai phải đang hoạt động và department phải thuộc hospital đã chọn.
4. Resolve role từ catalog hiện có, từ chối `PATIENT` và role có audit status `INACTIVE`. Alias `TECHNICIAN -> DIAGNOSTIC_STAFF`, `RECEPTIONIST -> RECEPTION`; `NURSE` chỉ hợp lệ khi UC58 đã tạo role `NURSE`.
5. LOCAL yêu cầu mật khẩu tối thiểu 8 ký tự có chữ hoa, chữ thường và số, sau đó hash bằng ASP.NET `PasswordHasher<User>`. GOOGLE không lưu password.
6. `ACTIVE` và `LOCKED` là trạng thái account. Staff profile mới là `ACTIVE`; khóa đăng nhập không đồng nghĩa hồ sơ nhân sự ngừng công tác.
7. Persist `User`, `Staff`, `UserRole` và audit `STAFF_ACCOUNT_CREATED` trong một transaction. Audit chỉ lưu ID/mã/phạm vi RBAC, không lưu password/hash, email hoặc họ tên.
8. Email được chuẩn hóa lowercase và unique toàn hệ thống; staff code chuẩn `STF-xxxx` và unique theo hospital. Pre-check trả `409`; DB unique constraint xử lý race và được dịch sang `409`.

## Consequences

- Duoc: route guard và authorization DB chống forged role claim; dữ liệu liên kết commit atomically; hợp đồng tương thích form hiện tại mà không tạo catalog ngầm.
- Mat: input tên catalog phụ thuộc chính tả; frontend nên chuyển sang ID/code khi tích hợp API catalog.
- Rui ro con lai: role `NURSE` chưa có trong seed nên lựa chọn này trả `400` cho tới khi được tạo qua UC58; GOOGLE login phụ thuộc auth provider flow riêng, ngoài scope issue #228.
