# 0047 — Hợp đồng API ngừng hoạt động và kích hoạt lại bác sĩ (`Deactivate & Reactivate Doctor` — `#188` / `M2-062` / `UC49`)

- Ngay: 2026-11-18
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#188` (`M2-062 — [M2][BE] Deactivate Doctor`, thuộc `UC49 Manage Doctor` / Module `Hospital Configuration`) yêu cầu hiện thực API, các quy tắc nghiệp vụ, lưu trữ, phân quyền hai lớp, kiểm tra dữ liệu đầu vào và xử lý lỗi cho hành vi ngừng hoạt động bác sĩ (Deactivate Doctor), kiểm tra toàn diện các lịch khám/Visit phụ thuộc, đồng thời hỗ trợ mở lại bác sĩ (Reactivate Doctor).

Trên nhánh `develop`:
1. Giao diện Frontend (`#187` / `M2-061`) đã được hiện thực tại `medflow-ai-frontend/src/features/admin-doctors/` (modal `DoctorDeactivateModal` - SCR-ADM-DOC-05, Figma node `2009-9653`):
   - Ngừng hoạt động bác sĩ: Bác sĩ không nhận lịch hoặc phân công mới; lịch sử khám bệnh, hồ sơ bệnh án, chỉ định và đơn thuốc vẫn được bảo toàn nguyên vẹn.
   - Điều kiện tiên quyết: Không thể ngừng hoạt động khi còn lượt khám đang xử lý hoặc lịch tương lai chưa được giải quyết; hiển thị cảnh báo `hasPendingAppointments` và `pendingAppointmentsCount`.
   - Nguyên tắc bảo toàn dữ liệu: Tuyệt đối không xóa cứng (No Hard Delete).
2. Cơ sở dữ liệu: Các bảng `doctors`, `staff`, `users`, `examination_slots`, `appointments`, `visits`, `audit_logs` đã có sẵn đầy đủ trong `database/medflow_schema_v4_1.sql`. Không cần tạo migration SQL mới.

## Decision

1. **Hợp đồng HTTP (`AdminDoctorsController`)**:
   - Định tuyến trên tiền tố `/api/v1/admin/doctors`:
     - **Ngừng hoạt động bác sĩ**: `POST|PATCH|PUT {idOrCode}/deactivate` hoặc `DELETE {idOrCode}` (`200 OK` -> `DeactivateDoctorResponseDto`).
     - **Kích hoạt lại bác sĩ**: `POST|PATCH|PUT {idOrCode}/reactivate` (`200 OK` -> `DeactivateDoctorResponseDto`).
   - `{idOrCode}`: Chấp nhận ID số nguyên dương (`1`, `2`) hoặc mã bác sĩ chuẩn (`"DOC-001"`, `"DOC-002"`).

2. **Phân quyền hai lớp & Kiểm tra quyền sở hữu (`401` / `403` / `404`)**:
   - **Lớp 1**: `[Authorize(Roles = "HOSPITAL_ADMIN,SYSTEM_ADMIN")]` bảo vệ controller. Người dùng chưa xác thực nhận `401 Unauthorized`; vai trò khác (như `DOCTOR`, `PATIENT`) bị từ chối ngay lập tức với `403 Forbidden`.
   - **Lớp 2 (`EnsureAdminAccessAsync`)**:
     - Kiểm tra tài khoản `User` hoạt động (`AccountStatus == ACTIVE`), không bị khóa (`LockedUntil <= nowUtc`).
     - `SYSTEM_ADMIN` có toàn quyền quản trị bác sĩ trên toàn hệ thống.
     - `HOSPITAL_ADMIN` chỉ được phép quản trị bác sĩ thuộc cơ sở y tế được gán (`doctorHospitalId == actorHospitalId`). Nếu thao tác trên bác sĩ thuộc cơ sở khác, hệ thống từ chối với `403 Forbidden` (`Bạn không có quyền thao tác trên hồ sơ bác sĩ thuộc cơ sở y tế khác.`).

3. **Kiểm tra dữ liệu đầu vào & Ràng buộc phụ thuộc (`400` / `404` / `409`)**:
   - **Định danh bác sĩ**: Nếu `{idOrCode}` không hợp lệ trả về `400 Bad Request`; nếu không tìm thấy bác sĩ trong hệ thống trả về `404 Not Found`.
   - **Trạng thái hiện tại**: Nếu bác sĩ đã ở trạng thái ngừng hoạt động (`IsActive == false`), thao tác deactivate trả về `409 Conflict` (`Bác sĩ đã ở trạng thái ngừng hoạt động từ trước.`).
   - **Kiểm tra ca khám đang hoạt động (`Active Ongoing Visits`)**:
     - Quét bảng `visits` có `AssignedDoctorId == doctor.Id` và trạng thái lâm sàng đang diễn ra (`WAITING`, `CALLED`, `IN_PROGRESS`, `IN_CONSULTATION`).
     - Nếu còn ca khám đang diễn ra: Không cho phép ngừng hoạt động bác sĩ, trả về `409 Conflict` yêu cầu hoàn thành hoặc điều chuyển ca khám trước.
   - **Kiểm tra khung giờ khám tương lai & Lịch hẹn phụ thuộc (`Future Slots & Active Appointments`)**:
     - Quét các khung giờ khám tương lai (`examination_slots`) của bác sĩ (`DoctorId == doctor.Id`, `EndsAt >= nowUtc`, `Status != "CLOSED"`).
     - Quét các lượt đặt lịch hẹn tương lai (`appointments`) gắn với các khung giờ này (`Status` là `BOOKED`, `CONFIRMED`, `CHECKED_IN`, hoặc chưa hủy).
     - **Chưa xác nhận (`ConfirmUnderstood == false`)**: Trả về `409 Conflict` kèm thông điệp chi tiết về số lượng lượt khám/khung giờ bị ảnh hưởng (`Không thể ngừng hoạt động khi còn {count} lượt khám đang xử lý hoặc lịch tương lai chưa được giải quyết.`), khớp chính xác giao diện modal Frontend #187.
     - **Đã xác nhận (`ConfirmUnderstood == true`)**: Hệ thống tự động đóng toàn bộ các khung giờ khám tương lai (`slot.Status = "CLOSED"`) và cập nhật trạng thái bác sĩ.

4. **Nguyên tắc bảo toàn dữ liệu & Chuyển trạng thái (No Hard Delete)**:
   - Tuyệt đối không xóa cứng bản ghi `Doctor` hay `Staff` khỏi cơ sở dữ liệu. Toàn bộ lịch sử phân công, hồ sơ bệnh án, chẩn đoán, chỉ định cận lâm sàng và đơn thuốc được bảo toàn 100%.
   - Chuyển `doctor.IsActive = false`.
   - Tự động ghi nhận `AuditLog` với `Action = "DOCTOR_DEACTIVATED"` hoặc `"DOCTOR_REACTIVATED"`.

5. **Kích hoạt lại bác sĩ (`ReactivateDoctorAsync`)**:
   - Nếu bác sĩ đang hoạt động (`IsActive == true`), trả về `409 Conflict`.
   - Nếu hồ sơ nhân sự gốc (`Staff`) đang ở trạng thái ngừng hoạt động (`Staff.Status != "ACTIVE"`), trả về `409 Conflict` (`Không thể kích hoạt bác sĩ khi hồ sơ nhân sự gốc đang ngừng hoạt động.`).
   - Thiết lập `doctor.IsActive = true`, ghi nhận nhật ký kiểm toán và trả về kết quả thành công.

## Consequences

- **Được**:
  - Hoàn thiện đầy đủ phạm vi `UC49` và issue `#188`, đồng bộ hoàn hảo với Frontend `#187`.
  - Phân quyền hai lớp chặt chẽ, ngăn chặn can thiệp chéo giữa các cơ sở y tế.
  - Xử lý toàn diện ràng buộc phụ thuộc (ca khám đang diễn ra, khung giờ khám tương lai, lịch hẹn chờ khám) mà không làm mất mát dữ liệu bệnh nhân.
  - Cung cấp đầy đủ dấu vết kiểm toán (`AuditLog`).
- **Mất**:
  - Không có. Không thay đổi schema DB hay migration mới.
