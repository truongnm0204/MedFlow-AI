# 0001 — Lưu trữ version và phân quyền cho cấu hình chính sách bệnh viện (`hospital_policies`)

- Ngay: 2026-10-30
- Trang thai: accepted
- Lane: high-risk

## Context
Use case UC54 (Configure Appointment Policy — M1-218) cho phép `HOSPITAL_ADMIN` xem và cập nhật các thông số giới hạn đặt lịch, đổi lịch, hủy lịch và xử lý vắng mặt (no-show) cho bệnh viện của mình.

Schema v4.1 đã có bảng `hospital_policies` (`id`, `hospital_id`, `queue_service_point_id`, `policy_type`, `policy_key`, `policy_value jsonb`, `policy_version`, `effective_from`, `effective_to`), và seed `003` mới khởi tạo 3/8 key của `APPOINTMENT` (`reschedule_hours`, `cancel_hours`, `max_active_bookings`). Ngoài ra:
- JWT hiện chỉ chứa `sub` và `ClaimTypes.Role`, không chứa `hospital_id`.
- Chưa có exception domain cho HTTP 403 khi kiểm tra quyền sở hữu/gán bệnh viện ở tầng service.

## Decision
1. **Lưu trữ bất biến theo version trong `hospital_policies`**:
   - Khi cập nhật một `policy_key` có thay đổi giá trị (so sánh theo giá trị số nguyên sau khi parse JSON + áp mặc định, hoặc khi DB chưa có dòng hiệu lực cho key đó), hệ thống đóng mọi dòng đang hiệu lực của key đó (`effective_to = now`) và chèn dòng mới với `policy_version = max(policy_version) + 1`, `effective_from = now`, `effective_to = null`.
   - Khi đọc cấu hình đang hiệu lực (`GetEffectiveAsync`), chỉ lấy các dòng có `(effective_from IS NULL OR effective_from <= now) AND (effective_to IS NULL OR effective_to > now)`, chọn dòng có `policy_version` lớn nhất (hòa thì lấy `id` lớn nhất).
   - Toàn bộ thay đổi policy và bản ghi `audit_logs` (`APPOINTMENT_POLICY_UPDATE`) được lưu trong một lần gọi `SaveChangesAsync`.
2. **Giá trị mặc định nằm trong code thay vì bắt buộc seed đủ mọi key**:
   - Nếu DB chưa có dòng hiệu lực cho một key hoặc JSON thiếu/hỏng trường, `AppointmentPolicyService` trả về hằng mặc định đã định nghĩa trong `AppointmentPolicyKeys`. Không cần tạo migration SQL để chèn thêm seed.
3. **Xác định bệnh viện phía server (Two-layer authorization)**:
   - Layer 1: `[Authorize(Roles = "HOSPITAL_ADMIN")]` tại `AppointmentPolicyController`.
   - Layer 2: `AppointmentPolicyService` tra cứu `staff.hospital_id` theo `user_id` (`status = 'ACTIVE'`). Request không nhận `hospitalId` từ client. Nếu tài khoản chưa được gán bệnh viện, ném `ForbiddenException`.
4. **Chuẩn hóa HTTP 403 qua `ForbiddenException`**:
   - Thêm `MedFlow.Domain.Exceptions.ForbiddenException` và map sang HTTP 403 `ProblemDetails` trong `ExceptionMiddleware`.

## Consequences
- Duoc:
  - Giữ đầy đủ lịch sử phiên bản cấu hình (`policy_version`, `effective_from`, `effective_to`) kèm `audit_logs` mà không cần sửa đổi schema DB.
  - Các tính năng đặt/đổi/hủy lịch về sau chỉ cần gọi `IAppointmentPolicyService.GetEffectiveAsync(hospitalId)` luôn nhận đủ 10 thông số hợp lệ kể cả với bệnh viện mới chưa cấu hình.
  - Đảm bảo quy tắc two-layer authorization, chống giả mạo `hospital_id` từ phía client.
- Mat:
  - Phải đọc các dòng `APPOINTMENT` của bệnh viện lên bộ nhớ để chọn version hiệu lực và tính `max(policy_version)`. Với số lượng key nhỏ (8 key) và tần suất thay đổi thấp, chi phí này không đáng kể.
- Rui ro con lai:
  - Bảng `hospital_policies` không có unique index trên `(hospital_id, policy_type, policy_key)` khi `effective_to IS NULL`. Nếu hai admin của cùng một bệnh viện cập nhật đồng thời cùng một key, có thể sinh hai dòng cùng hiệu lực; đã giảm thiểu bằng quy tắc chọn `OrderByDescending(PolicyVersion).ThenByDescending(Id)` khi đọc và đóng tất cả dòng đang hiệu lực của key khi ghi lần kế tiếp.
