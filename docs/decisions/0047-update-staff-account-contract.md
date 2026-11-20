# 0047 — Hợp đồng cập nhật tài khoản nhân sự

- Ngày: 2026-11-16
- Trạng thái: accepted
- Lane: high-risk

## Context

Issue `#230` / `M2-070` / `UC57` yêu cầu `SYSTEM_ADMIN` cập nhật thông tin đăng nhập, hồ sơ, vai trò và trạng thái tài khoản nhân sự. Frontend hiện dùng mã nhân sự trên route và gửi `fullName`, `authMethod`, `facility`, `email`, `status`, `role`, `department`. Schema hiện có đã cung cấp đầy đủ `users`, `staff`, `roles`, `user_roles`, `hospitals`, `departments` và `audit_logs`; không cần migration.

Issue `#232` tách riêng thao tác vô hiệu hóa vì cần kiểm tra dependency vận hành. Do đó API cập nhật không được dùng để chuyển một tài khoản đang hoạt động sang `DISABLED`.

## Decision

1. Mở `PUT` và `PATCH /api/v1/admin/staff-accounts/{staffIdentifier}` cùng alias `/api/v1/system-admin/staff-accounts/{staffIdentifier}`. Identifier nhận staff ID số dương hoặc staff code; mã nhân sự không được đổi. Staff code chỉ được chấp nhận khi xác định duy nhất trên toàn hệ thống; nếu trùng giữa các cơ sở, client phải dùng ID.
2. Request hỗ trợ cập nhật từng trường và giữ JSON field `authMethod` theo frontend. Ít nhất một trường phải có mặt. Response không trả password/hash và cho phép facility/department nullable đối với hồ sơ chưa được phân công.
3. Phân quyền hai lớp:
   - Controller yêu cầu claim `SYSTEM_ADMIN`.
   - Service xác minh actor tồn tại, `ACTIVE`, không bị khóa, có assignment `SYSTEM_ADMIN`, và staff profile của actor (nếu có) đang `ACTIVE`.
4. Chống khóa quyền quản trị: API nhân sự không được đổi vai trò, trạng thái hoặc auth provider của bất kỳ tài khoản `SYSTEM_ADMIN` nào. Các thay đổi hồ sơ không làm mất quyền vẫn được phép.
5. Vai trò staff là một assignment nghiệp vụ duy nhất trên contract này. Update thay assignment hiện tại bằng role đã chọn; từ chối `PATIENT` và `SYSTEM_ADMIN`. Alias `TECHNICIAN -> DIAGNOSTIC_STAFF`, `RECEPTIONIST -> RECEPTION`. Role không tồn tại hoặc có trạng thái audit `INACTIVE` bị từ chối.
6. `ACTIVE` và `LOCKED` là trạng thái hợp lệ. `DISABLED` chỉ được giữ nguyên cho tài khoản đã vô hiệu hóa; mọi chuyển đổi vào/ra `DISABLED` phải qua flow chuyên biệt. Chuyển sang `ACTIVE` xóa `locked_until` và số lần login sai; chuyển sang `LOCKED` đặt trạng thái khóa nhưng không ngừng hồ sơ nhân sự. Mọi thay đổi role/status/auth method/email thu hồi refresh session đang hoạt động; login LOCAL và refresh đều từ chối provider/trạng thái không hợp lệ.
7. Facility/department được resolve theo code ưu tiên hoặc tên không phân biệt hoa thường. Tên trùng bị từ chối và yêu cầu dùng code. Khi cập nhật scope, cả hai catalog phải active và department phải thuộc facility. Partial update không chạm scope được phép với hồ sơ chưa phân công.
8. Email chuẩn hóa lowercase và unique. Chuyển sang `LOCAL` yêu cầu password mạnh mới; chuyển sang `GOOGLE` xóa local password hash. Auth provider không đổi thì password không bị chạm.
9. Persist thay đổi user/staff/role/session và audit `STAFF_ACCOUNT_UPDATED` trong một transaction. Audit chứa ID, mã và trạng thái vận hành trước/sau; không chứa email, họ tên, password hoặc hash. PostgreSQL `xmin` của `staff` là concurrency token để scope update đồng thời trả `409` thay vì tạo cặp facility/department sai.

## Consequences

- Được: đáp ứng form hiện tại, chống forged role claim, self-lockout, role escalation và bypass flow vô hiệu hóa.
- Mất: frontend phải gửi password nếu chuyển tài khoản GOOGLE sang LOCAL; form hiện tại chưa cung cấp trường này.
- Rủi ro còn lại: contract vai trò đơn sẽ thay mọi assignment staff hiện có; tài khoản `SYSTEM_ADMIN` được bảo vệ và không đi qua nhánh này.
