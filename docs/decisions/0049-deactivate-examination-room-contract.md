# 0049 — Hợp đồng API ngừng hoạt động và kích hoạt lại buồng khám (`Deactivate & Reactivate Examination Room` — `#180` / `M2-060` / `UC48`)

- Ngay: 2026-11-22
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#180` (`M2-060 — [M2][BE] Deactivate Examination Room`, thuộc `UC48 Manage Examination Room` / Module `Hospital Configuration`) yêu cầu hiện thực API, các quy tắc nghiệp vụ, lưu trữ, phân quyền hai lớp, kiểm tra dữ liệu đầu vào và xử lý lỗi cho hành vi ngừng hoạt động buồng khám (Deactivate Examination Room), kiểm tra toàn diện các dependency đang hoạt động (lượt khám đang diễn ra, bác sĩ trực/khám, ca khám tương lai và lịch hẹn), đồng thời hỗ trợ mở lại buồng khám (Reactivate Examination Room).

Trên nhánh `develop`:
1. Giao diện Frontend (`#179` / `M2-059`) đã được hiện thực tại `medflow-ai-frontend/src/features/admin-rooms/` (modal `RoomDeactivateModal` - SCR-ADM-ROOM-05, trang danh sách buồng khám `room-list-page.tsx`, bảng buồng khám `room-table.tsx`):
   - Ngừng hoạt động buồng khám: Buồng khám chuyển sang trạng thái Ngừng hoạt động (`INACTIVE`); không nhận lượt khám mới hoặc phân công ca khám mới; lịch sử khám bệnh, chỉ định cận lâm sàng và hồ sơ bệnh án vẫn được lưu trữ nguyên vẹn.
   - Điều kiện tiên quyết: Không thể ngừng hoạt động khi còn bác sĩ đang trực (`assignedDoctorsCount`), lượt khám đang xử lý (`activeVisitsCount`) hoặc ca khám/lịch hẹn đang mở (`activeSlotsCount`); hiển thị thông báo chặn rõ ràng `blockingReason`.
   - Nguyên tắc bảo toàn dữ liệu: Tuyệt đối không xóa cứng (No Hard Delete).
2. Cơ sở dữ liệu: Các bảng `hospital_rooms`, `departments`, `doctors`, `staff`, `visits`, `appointments`, `examination_slots`, `audit_logs` đã có sẵn đầy đủ trong schema. Không cần tạo migration SQL mới.

## Decision

1. **Hợp đồng HTTP (`AdminExaminationRoomsController`)**:
   - Định tuyến trên tiền tố `/api/v1/admin/examination-rooms`, `/api/v1/admin/rooms`, và alias `/api/v1/examination-rooms`:
     - **Kiểm tra ràng buộc phụ thuộc**: `GET {idOrCode}/dependencies` (`200 OK` -> `ExaminationRoomDependencySummaryDto`).
     - **Ngừng hoạt động buồng khám**: `POST|PATCH|PUT {idOrCode}/deactivate` hoặc `DELETE {idOrCode}` (`200 OK` -> `DeactivateExaminationRoomResponseDto`).
     - **Kích hoạt lại buồng khám**: `POST|PATCH|PUT {idOrCode}/reactivate` (`200 OK` -> `DeactivateExaminationRoomResponseDto`).
   - `{idOrCode}`: Chấp nhận ID số nguyên dương (`1`, `2`) hoặc mã buồng khám chuẩn (`"RM-101"`, `"P201"`).

2. **Phân quyền hai lớp & Kiểm tra quyền sở hữu (`401` / `403` / `404`)**:
   - **Lớp 1**: `[Authorize(Roles = "HOSPITAL_ADMIN,SYSTEM_ADMIN")]` bảo vệ controller. Người dùng chưa xác thực nhận `401 Unauthorized`; vai trò khác (như `DOCTOR`, `PATIENT`) bị từ chối ngay lập tức với `403 Forbidden`.
   - **Lớp 2 (`EnsureAdminAccessAsync`)**:
     - Kiểm tra tài khoản `User` hoạt động (`AccountStatus == ACTIVE`), không bị khóa (`LockedUntil <= nowUtc`).
     - `SYSTEM_ADMIN` có toàn quyền quản trị buồng khám trên toàn hệ thống.
     - `HOSPITAL_ADMIN` chỉ được phép quản trị buồng khám thuộc cơ sở y tế được gán (`room.HospitalId == actorHospitalId`). Nếu thao tác trên buồng khám thuộc cơ sở khác, hệ thống từ chối với `403 Forbidden` (`Bạn không có quyền thao tác trên buồng khám thuộc cơ sở y tế khác.`).

3. **Kiểm tra dữ liệu đầu vào & Ràng buộc phụ thuộc (`400` / `404` / `409`)**:
   - **Định danh buồng khám**: Nếu `{idOrCode}` không hợp lệ trả về `400 Bad Request`; nếu không tìm thấy buồng khám trả về `404 Not Found`.
   - **Trạng thái hiện tại**: Nếu buồng khám đã ở trạng thái ngừng hoạt động (`Status == "INACTIVE"`), thao tác deactivate trả về `409 Conflict` (`Buồng khám '{room.Name}' ({room.RoomCode}) hiện đã ở trạng thái ngừng hoạt động.`).
   - **Kiểm tra lượt khám đang xử lý (`Active Visits`)**:
     - Quét bảng `visits` có `ExaminationRoomId == room.Id` và trạng thái lâm sàng đang diễn ra (`WAITING`, `CALLED`, `IN_PROGRESS`, `IN_CONSULTATION`).
     - Nếu còn lượt khám đang xử lý: Chặn vô hiệu hóa, trả về `409 Conflict` yêu cầu hoàn tất hoặc điều chuyển buồng khám trước.
   - **Kiểm tra bác sĩ đang trực / phân công (`Assigned Doctors`)**:
     - Quét các bác sĩ được phân công vào ca khám tương lai/đang mở tại phòng (`ExaminationSlots.RoomId == room.Id`) hoặc đang phụ trách ca khám diễn ra tại phòng (`Visits.ExaminationRoomId == room.Id`).
     - Nếu còn bác sĩ đang trực: Chặn vô hiệu hóa, trả về `409 Conflict`.
   - **Kiểm tra ca khám và lịch hẹn đang mở (`Active Slots & Pending Appointments`)**:
     - Quét các ca khám `examination_slots` của buồng khám chưa đóng (`Status != "CLOSED"` và `Status != "CANCELLED"`).
     - Quét các lượt đặt lịch `appointments` của bệnh nhân trong các ca khám này (`BOOKED`, `CONFIRMED`, `CHECKED_IN`).
     - Nếu còn lịch hẹn chưa hoàn tất: Chặn vô hiệu hóa, trả về `409 Conflict`.
   - **Cơ chế xác nhận (`ConfirmUnderstood`)**:
     - Trường hợp không còn lượt khám đang xử lý hay lịch hẹn của bệnh nhân, nhưng còn các khung giờ khám tương lai chưa đóng: Cho phép Hospital Admin gửi kèm `ConfirmUnderstood = true` để hệ thống tự động đóng các ca khám tương lai (`slot.Status = "CLOSED"`) và hoàn tất vô hiệu hóa. Nếu chưa xác nhận (`ConfirmUnderstood == false`), trả về `409 Conflict` kèm thông điệp cảnh báo.

4. **Nguyên tắc bảo toàn dữ liệu & Chuyển trạng thái (No Hard Delete)**:
   - Tuyệt đối không xóa cứng bản ghi `HospitalRoom` khỏi cơ sở dữ liệu. Toàn bộ lịch sử ca khám, chỉ định cận lâm sàng, hồ sơ bệnh án và nhật ký tiếp đón được bảo toàn nguyên vẹn.
   - Cập nhật `room.Status = "INACTIVE"`.
   - Tự động ghi nhận `AuditLog` với `Action = "ROOM_DEACTIVATED"` hoặc `"ROOM_REACTIVATED"`.

5. **Kích hoạt lại buồng khám (`ReactivateExaminationRoomAsync`)**:
   - Nếu buồng khám đang hoạt động (`Status == "ACTIVE"`), trả về `409 Conflict` (`Buồng khám '{room.Name}' ({room.RoomCode}) đang ở trạng thái hoạt động.`).
   - Thiết lập `room.Status = "ACTIVE"`, ghi nhận nhật ký kiểm toán `ROOM_REACTIVATED` và trả về kết quả thành công.

## Consequences

- **Được**:
  - Hoàn thiện đầy đủ phạm vi `UC48` và issue `#180`, đồng bộ hoàn hảo với Frontend `#179` (`RoomDeactivateModal`).
  - Phân quyền hai lớp chặt chẽ, ngăn chặn can thiệp chéo giữa các cơ sở y tế.
  - Xử lý toàn diện ràng buộc phụ thuộc (lượt khám, bác sĩ, ca khám và lịch hẹn) đảm bảo an toàn vận hành bệnh viện.
  - Bảo toàn toàn vẹn dữ liệu lâm sàng và cung cấp đầy đủ dấu vết kiểm toán (`AuditLog`).
- **Mất**:
  - Không có. Không thay đổi schema DB hay migration mới.
