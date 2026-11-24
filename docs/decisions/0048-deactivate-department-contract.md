# 0048 — Hợp đồng API ngừng hoạt động và kích hoạt lại khoa phòng (`Deactivate & Reactivate Department` — `#172` / `M2-058` / `UC47`)

- Ngay: 2026-11-22
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#172` (`M2-058 — [M2][BE] Deactivate Department`, thuộc `UC47 Manage Department` / Module `Hospital Configuration`) yêu cầu hiện thực API, các quy tắc nghiệp vụ, lưu trữ, phân quyền hai lớp, kiểm tra dữ liệu đầu vào và xử lý lỗi cho hành vi ngừng hoạt động khoa phòng (Deactivate Department), kiểm tra toàn diện các dependency đang hoạt động (buồng khám, nhân sự bác sĩ, dịch vụ khám, ca khám và lịch hẹn), đồng thời hỗ trợ mở lại khoa phòng (Reactivate Department).

Trên nhánh `develop`:
1. Giao diện Frontend (`#171` / `M2-057`) đã được hiện thực tại `medflow-ai-frontend/src/features/admin-departments/` (modal `DepartmentDeactivateModal` - SCR-ADM-DEP-03, API `checkDepartmentDependenciesApi`, `deactivateDepartmentApi`, `reactivateDepartmentApi`):
   - Ngừng hoạt động khoa phòng: Khoa phòng chuyển sang trạng thái Tạm ngừng; các dịch vụ khám và buồng khám trực thuộc không thể tiếp nhận lượt khám mới; lịch sử khám bệnh, hồ sơ bệnh án và danh mục vẫn được bảo toàn nguyên vẹn.
   - Điều kiện tiên quyết: Không thể ngừng hoạt động khi còn buồng khám (`activeRoomsCount`), nhân sự bác sĩ (`activeDoctorsCount`) hoặc lịch khám/ca khám (`pendingAppointmentsCount`) đang hoạt động; hiển thị thông báo chặn rõ ràng `blockingReason`.
   - Nguyên tắc bảo toàn dữ liệu: Tuyệt đối không xóa cứng (No Hard Delete).
2. Cơ sở dữ liệu: Các bảng `departments`, `hospital_rooms`, `services`, `doctors`, `staff`, `visits`, `appointments`, `examination_slots`, `audit_logs` đã có sẵn đầy đủ trong schema. Không cần tạo migration SQL mới.

## Decision

1. **Hợp đồng HTTP (`AdminDepartmentsController`)**:
   - Định tuyến trên tiền tố `/api/v1/admin/departments` (và alias `/api/v1/departments`):
     - **Kiểm tra ràng buộc phụ thuộc**: `GET {idOrCode}/dependencies` (`200 OK` -> `DepartmentDependencySummaryDto`).
     - **Ngừng hoạt động khoa phòng**: `POST|PATCH|PUT {idOrCode}/deactivate` hoặc `DELETE {idOrCode}` (`200 OK` -> `DeactivateDepartmentResponseDto`).
     - **Kích hoạt lại khoa phòng**: `POST|PATCH|PUT {idOrCode}/reactivate` (`200 OK` -> `DeactivateDepartmentResponseDto`).
   - `{idOrCode}`: Chấp nhận ID số nguyên dương (`1`, `2`) hoặc mã khoa phòng chuẩn (`"KKB"`, `"KPT"`, `"KNOI"`).

2. **Phân quyền hai lớp & Kiểm tra quyền sở hữu (`401` / `403` / `404`)**:
   - **Lớp 1**: `[Authorize(Roles = "HOSPITAL_ADMIN,SYSTEM_ADMIN")]` bảo vệ controller. Người dùng chưa xác thực nhận `401 Unauthorized`; vai trò khác (như `DOCTOR`, `PATIENT`) bị từ chối ngay lập tức với `403 Forbidden`.
   - **Lớp 2 (`EnsureAdminAccessAsync`)**:
     - Kiểm tra tài khoản `User` hoạt động (`AccountStatus == ACTIVE`), không bị khóa (`LockedUntil <= nowUtc`).
     - `SYSTEM_ADMIN` có toàn quyền quản trị khoa phòng trên toàn hệ thống.
     - `HOSPITAL_ADMIN` chỉ được phép quản trị khoa phòng thuộc cơ sở y tế được gán (`dept.HospitalId == actorHospitalId`). Nếu thao tác trên khoa phòng thuộc cơ sở khác, hệ thống từ chối với `403 Forbidden` (`Bạn không có quyền thao tác trên khoa phòng thuộc cơ sở y tế khác.`).

3. **Kiểm tra dữ liệu đầu vào & Ràng buộc phụ thuộc (`400` / `404` / `409`)**:
   - **Định danh khoa phòng**: Nếu `{idOrCode}` không hợp lệ trả về `400 Bad Request`; nếu không tìm thấy khoa phòng trả về `404 Not Found`.
   - **Trạng thái hiện tại**: Nếu khoa phòng đã ở trạng thái ngừng hoạt động (`IsActive == false`), thao tác deactivate trả về `409 Conflict` (`Khoa phòng '{dept.Name}' ({dept.DepartmentCode}) hiện đã ở trạng thái ngừng hoạt động.`).
   - **Kiểm tra buồng khám đang hoạt động (`Active Rooms`)**:
     - Quét bảng `hospital_rooms` có `DepartmentId == dept.Id` và `IsActive == true` (hoặc `Status == "ACTIVE"`).
     - Nếu còn buồng khám đang hoạt động: Chặn vô hiệu hóa, trả về `409 Conflict` yêu cầu ngừng hoặc điều chuyển buồng khám trước.
   - **Kiểm tra bác sĩ đang hoạt động (`Active Doctors`)**:
     - Quét bảng `doctors` gắn với nhân sự `staff` có `DepartmentId == dept.Id` và `IsActive == true`.
     - Nếu còn bác sĩ đang hoạt động: Chặn vô hiệu hóa, trả về `409 Conflict`.
   - **Kiểm tra ca khám và lịch hẹn đang hoạt động (`Active Visits & Pending Appointments`)**:
     - Quét các ca khám `visits` đang diễn ra (`WAITING`, `CALLED`, `IN_PROGRESS`, `IN_CONSULTATION`) liên quan đến phòng khám hoặc bác sĩ thuộc khoa.
     - Quét các lượt đặt lịch `appointments` tương lai (`BOOKED`, `CONFIRMED`, `CHECKED_IN`) gắn với dịch vụ hoặc buồng khám của khoa.
     - Nếu còn lịch khám / ca khám chưa hoàn tất: Chặn vô hiệu hóa, trả về `409 Conflict`.
   - **Cơ chế xác nhận (`ConfirmUnderstood`)**:
     - Trường hợp không còn buồng khám và bác sĩ hoạt động, nhưng còn các khung giờ khám tương lai chưa đóng: Cho phép Hospital Admin gửi kèm `ConfirmUnderstood = true` để hệ thống tự động đóng các khung giờ khám tương lai (`slot.Status = "CLOSED"`) và hoàn tất vô hiệu hóa. Nếu chưa xác nhận (`ConfirmUnderstood == false`), trả về `409 Conflict` kèm thông điệp cảnh báo.

4. **Nguyên tắc bảo toàn dữ liệu & Chuyển trạng thái (No Hard Delete)**:
   - Tuyệt đối không xóa cứng bản ghi `Department` khỏi cơ sở dữ liệu. Toàn bộ lịch sử ca khám, chỉ định cận lâm sàng, hồ sơ bệnh án và danh mục viện phí được bảo toàn nguyên vẹn.
   - Cập nhật `dept.IsActive = false`.
   - Tự động ghi nhận `AuditLog` với `Action = "DEPARTMENT_DEACTIVATED"` hoặc `"DEPARTMENT_REACTIVATED"`.

5. **Kích hoạt lại khoa phòng (`ReactivateDepartmentAsync`)**:
   - Nếu khoa phòng đang hoạt động (`IsActive == true`), trả về `409 Conflict` (`Khoa phòng đang ở trạng thái hoạt động.`).
   - Thiết lập `dept.IsActive = true`, ghi nhận nhật ký kiểm toán `DEPARTMENT_REACTIVATED` và trả về kết quả thành công.

## Consequences

- **Được**:
  - Hoàn thiện đầy đủ phạm vi `UC47` và issue `#172`, đồng bộ hoàn hảo với Frontend `#171` (`DepartmentDeactivateModal`).
  - Phân quyền hai lớp chặt chẽ, ngăn chặn can thiệp chéo giữa các cơ sở y tế.
  - Xử lý toàn diện ràng buộc phụ thuộc (buồng khám, bác sĩ, dịch vụ, ca khám và lịch hẹn) đảm bảo an toàn vận hành bệnh viện.
  - Bảo toàn toàn vẹn dữ liệu lâm sàng và cung cấp đầy đủ dấu vết kiểm toán (`AuditLog`).
- **Mất**:
  - Không có. Không thay đổi schema DB hay migration mới.
