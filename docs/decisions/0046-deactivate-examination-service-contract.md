# 0046 — Hợp đồng API ngừng hoạt động và mở lại dịch vụ khám (`Deactivate & Reactivate Examination Service` — `#196` / `M2-064` / `UC50`)

- Ngày: 2026-11-16
- Trạng thái: accepted
- Lane: high-risk

## Context

Issue `#196` (`M2-064 — [M2][BE] Deactivate Examination Service`, thuộc `UC50 Manage Examination Service`) yêu cầu cung cấp các API và quy tắc nghiệp vụ cho phép quản trị viên bệnh viện (`HOSPITAL_ADMIN`) hoặc quản trị viên hệ thống (`SYSTEM_ADMIN`) ngừng hoạt động (deactivate / soft-delete) các dịch vụ khám bệnh không còn cung cấp, kiểm tra và xử lý cảnh báo phụ thuộc khi dịch vụ đang có khung giờ khám hoạt động hoặc lịch hẹn của bệnh nhân đang chờ tiếp nhận, và cho phép kích hoạt lại dịch vụ khi cần.

Trên nhánh `develop`:
1. Giao diện Frontend (`#195` / `M2-063`) đã hoàn thiện modal xác nhận ngừng hoạt động tại `medflow-ai-frontend/src/features/admin-services/components/service-deactivate-modal.tsx`:
   - Hiển thị thông tin dịch vụ, cảnh báo điều kiện tiên quyết nếu còn lịch hẹn đang xử lý (`hasActiveBookings`, `activeBookingsCount`).
   - Khẳng định nguyên tắc bảo toàn dữ liệu: "Xóa cứng không áp dụng: hồ sơ danh mục và mọi liên kết lịch sử được bảo toàn để kiểm toán và đối soát viện phí."
2. Bảng `services`, `examination_slots`, `appointments` và `audit_logs` trong cơ sở dữ liệu đã có sẵn các trường cần thiết (`services.is_active`, `examination_slots.status`, `audit_logs`). Không yêu cầu thay đổi schema DB.

## Decision

1. **Hợp đồng HTTP (`AdminExaminationServicesController`)**:
   - Hỗ trợ đầy đủ các tiền tố route chuẩn `/api/v1/admin/examination-services`, `/api/v1/admin/services` và `/api/v1/examination-services`:
     - **Ngừng hoạt động**:
       - `POST|PATCH|PUT {idOrCode}/deactivate`
       - `DELETE {idOrCode}`
       - Trả về `200 OK` -> `DeactivateExaminationServiceResponseDto`.
     - **Kích hoạt lại**:
       - `POST|PATCH|PUT {idOrCode}/reactivate`
       - Trả về `200 OK` -> `DeactivateExaminationServiceResponseDto`.
   - `{idOrCode}` chấp nhận ID số nguyên (`1`, `"1"`) hoặc mã dịch vụ (`"EX-1-01"`, `"KKB-01"`).

2. **Phân quyền hai lớp (`401` / `403` / `404`)**:
   - **Lớp 1**: `[Authorize(Roles = "HOSPITAL_ADMIN,SYSTEM_ADMIN")]` trên Controller.
   - **Lớp 2 (`EnsureAdminAccessAsync`)**:
     - Xác minh `actorUserId > 0` (`401`), tài khoản `User` tồn tại và `ACTIVE` (`403`), chưa bị tạm khóa (`403`).
     - Đối với `HOSPITAL_ADMIN`: bắt buộc có hồ sơ `Staff` đang `ACTIVE` gắn với `Hospital` đang hoạt động và chỉ được phép thao tác trên các dịch vụ thuộc đúng `HospitalId` của mình (khác cơ sở trả `403 Forbidden`).

3. **Kiểm tra dữ liệu & Xử lý phụ thuộc (`400` / `404` / `409`)**:
   - **Kiểm tra tồn tại**: Dịch vụ không tồn tại trả về `404 Not Found`.
   - **Kiểm tra trạng thái hiện tại**:
     - Ngừng hoạt động dịch vụ đã ở trạng thái `IsActive == false` trả về `409 Conflict`.
     - Kích hoạt lại dịch vụ đang ở trạng thái `IsActive == true` trả về `409 Conflict`.
   - **Kiểm tra phụ thuộc (Dependency Check)**:
     - Dịch vụ có khung giờ khám trong tương lai chưa đóng (`StartsAt/EndsAt >= nowUtc` và `Status != "CLOSED"`).
     - Dịch vụ có lịch hẹn khám đang chờ khám (`Status != "CANCELLED" && Status != "NO_SHOW" && Status != "COMPLETED"`).
     - Nếu còn lịch hẹn phụ thuộc mà người dùng chưa xác nhận (`ConfirmUnderstood == false`): Hệ thống trả về `409 Conflict` kèm thông điệp cảnh báo số lượng lịch hẹn phụ thuộc đang chờ xử lý.
     - Khi đã xác nhận (`ConfirmUnderstood == true` hoặc `CloseFutureSlots == true`): Tự động đóng các khung giờ tương lai chưa đóng (`Status = "CLOSED"`) và cập nhật `service.IsActive = false`. Tuyệt đối không xóa cứng dữ liệu.

4. **Nhật ký kiểm toán (`AuditLog`) & Bổ sung cờ phụ thuộc cho danh sách**:
   - Mỗi thao tác ngừng hoạt động hoặc kích hoạt lại ghi một bản ghi `AuditLog` (`EntityType = "Service"`, `Action = "EXAMINATION_SERVICE_DEACTIVATED"` hoặc `"EXAMINATION_SERVICE_REACTIVATED"`).
   - Bổ sung `HasActiveBookings` và `ActiveBookingsCount` vào `ExaminationServiceAdminItemDto` để Frontend hiển thị trạng thái chuẩn xác ngay trên bảng và modal.

## Consequences

- **Được**:
  - Đáp ứng trọn vẹn nghiệp vụ `UC50` (#196) và tương thích hoàn toàn với modal Frontend `#195`.
  - Đảm bảo an toàn dữ liệu, ngăn chặn đặt lịch vào dịch vụ ngừng hoạt động mà vẫn bảo toàn hồ sơ bệnh án và lịch sử kiểm toán.
- **Mất**:
  - Khi truy vấn danh sách dịch vụ, hệ thống thực hiện một truy vấn nhóm đếm lịch hẹn phụ thuộc cho trang hiện tại (tối đa 100 dịch vụ).
