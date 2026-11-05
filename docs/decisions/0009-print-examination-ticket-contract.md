# 0009 — Hợp đồng API In Phiếu Khám Bệnh Ngoại Trú (`UC29` / `#84` / `M2-032`)

- Ngay: 2026-11-05
- Trang thai: accepted
- Lane: high-risk

## Context
Issue `#84` (`M2-032 — [M2][BE] Print Examination Ticket` / `UC29`) yêu cầu cung cấp Backend API và quy tắc nghiệp vụ cho phép nhân viên tiếp đón (`RECEPTION`) xem trước và thực hiện in phiếu khám bệnh ngoại trú sau khi hoàn tất tiếp nhận bệnh nhân (theo lịch hẹn `SCHEDULED` hoặc vãng lai `WALK_IN`) và đã phân công bác sĩ phụ trách (`UC26`) cùng buồng khám (`UC27`).

Bảng `visits`, `queue_entries`, `appointments`, `examination_slots`, `patients`, `doctors`, `hospital_rooms`, `departments`, `specialties`, `services`, `staff`, `hospitals`, và `audit_logs` đã có đầy đủ cột dữ liệu cần thiết trong `database/medflow_schema_v4_1.sql` và `003_v41_priority1_seed.sql`.

## Decision
1. **Hợp đồng HTTP (`ReceptionVisitsController`)**:
   - Xem trước thông tin phiếu khám (Read-only, không ghi `audit_logs`):
     - `GET /api/v1/reception/visits/{idOrCode}/ticket` (canonical)
     - `GET /api/v1/reception/visits/{idOrCode}/examination-ticket`
     - `GET /api/v1/visits/{idOrCode}/ticket`
     - `GET /api/v1/visits/{idOrCode}/examination-ticket`
   - Thực hiện lệnh in phiếu khám và ghi nhận kiểm toán (`audit_logs`):
     - `POST /api/v1/reception/visits/{idOrCode}/ticket/print` (canonical)
     - `POST /api/v1/reception/visits/{idOrCode}/ticket`
     - `POST /api/v1/reception/visits/{idOrCode}/examination-ticket/print`
     - `POST /api/v1/reception/visits/{idOrCode}/examination-ticket`
   - Request body tùy chọn (`PrintExaminationTicketRequest`):
     - `Copies` (`int?`, mặc định `1`, hợp lệ `1..5`)
     - `IsReprint` (`bool?`, tùy chọn)
     - `Reason` / `Note` (`string?`, tối đa `250` ký tự)
   - Response `200 OK` (`ExaminationTicketDto`):
     - Định danh lượt khám & phiếu: `visitId`, `visitCode`, `ticketCode`, `ticketNo`, `queueNumber`, `queueNumberFormatted`, `queueEntryId`, `queueDate`, `queueStatus`, `priority`, `priorityLabel`, `waitingAheadCount`, `estimatedWaitMinutes`, `visitType`, `visitTypeLabel`, `visitStatus`, `visitStatusLabel`, `appointmentId`, `appointmentCode`, `scheduledTimeRange`.
     - Thông tin bệnh nhân (đã loại bỏ `identification_hash`): `patientId`, `patientCode`, `patientName`, `patientDateOfBirth`, `patientBirthYear`, `patientAge`, `patientGender`, `patientGenderLabel`, `patientPhone`.
     - Cơ sở y tế, khoa/chuyên khoa, buồng khám (`UC27`), bác sĩ (`UC26`), dịch vụ: `hospitalId`, `hospitalCode`, `hospitalName`, `hospitalAddress`, `departmentId`, `departmentCode`, `departmentName`, `specialtyId`, `specialtyCode`, `specialtyName`, `roomId`, `roomCode`, `roomName`, `roomLocation`, `doctorId`, `doctorCode`, `doctorTitle`, `doctorName`, `serviceId`, `serviceCode`, `serviceName`, `reasonForVisit`.
     - Thông tin tiếp nhận & kiểm toán in ấn: `checkedInAt`, `checkedInAtLabel`, `checkedInByStaffId`, `checkedInByStaffName`, `printedAtUtc`, `printedAtLabel`, `printedByUserId`, `printedByStaffCode`, `printedByStaffName`, `copies`, `printCount`, `isReprint`, `guidanceNote`.

2. **Phân quyền 2 lớp (Two-Layer Authorization) và Cô lập phạm vi Cơ sở y tế**:
   - **Layer 1**: `[Authorize(Roles = "RECEPTION")]` trên `ReceptionVisitsController`.
   - **Layer 2 (`VisitTicketService`)**:
     - Xác thực `actorUserId > 0` (`401`), tài khoản `User` tồn tại (`404`), `AccountStatus == ACTIVE` và không bị khóa `LockedUntil` (`403`).
     - Đối chiếu bảng `user_roles` / `roles` trong DB phải có vai trò `RECEPTION` và có ít nhất một quyền nghiệp vụ tiếp đón (`PATIENT_CHECKIN`, `VISIT_ASSIGN`, hoặc `QUEUE_MANAGE`) khi `role_permissions` được cấu hình (`403`).
     - Hồ sơ `Staff` của nhân viên tiếp đón phải `ACTIVE` và gắn với `Hospital` đang hoạt động (`403` / `404`).
     - Kiểm tra quyền sở hữu theo `HospitalId`: mọi thực thể gắn với `Visit` (`HospitalRoom`, `Department`, `Doctor`, `Staff` tiếp nhận, `ExaminationSlot`) phải thuộc cùng `HospitalId` với nhân viên tiếp đón; nếu khác cơ sở y tế trả về `403 Forbidden`.

3. **Kiểm tra điều kiện tiên quyết `UC26` và `UC27` (`409 Conflict`)**:
   - `Visit.Status` là `CANCELLED` hoặc `NO_SHOW` → `409 Conflict`.
   - `Visit.AssignedDoctorId == null` (chưa hoàn tất `UC26`) hoặc bác sĩ được gán đang ngừng hoạt động (`!Doctor.IsActive`) → `409 Conflict`.
   - `Visit.ExaminationRoomId == null` (chưa hoàn tất `UC27`) hoặc buồng khám không ở trạng thái `ACTIVE` (`MAINTENANCE` / `INACTIVE`) → `409 Conflict`.
   - Hàng đợi khám `QueueEntry` (`QueueType == "EXAMINATION"`) của lượt khám ở trạng thái `CANCELLED` → `409 Conflict`.

4. **Kiểm toán lệnh in (`AuditLog`) không cần thay đổi Schema DB**:
   - Thao tác `GET` chỉ đọc và đếm số bản ghi `audit_logs` hiện có (`EntityType = "VISIT"`, `Action = "PRINT_TICKET"`, `EntityId = visit.Id.ToString()`).
   - Thao tác `POST` ghi thêm 1 bản ghi `AuditLog` với `Action = "PRINT_TICKET"`, `EntityType = "VISIT"`, `HospitalId`, `ActorUserId`, `IpAddress`, và `ChangeData` JSON chứa `visitCode`, `ticketCode`, `ticketNo`, `queueNumber`, `copies`, `printCount`, `isReprint`.

## Consequences
- Duoc:
  - Đáp ứng trọn vẹn `UC29` cho cả bệnh nhân đặt lịch trước (`SCHEDULED`) và bệnh nhân vãng lai (`WALK_IN`), bảo đảm các điều kiện tiên quyết `UC26` và `UC27`.
  - Tái sử dụng 100% schema v4.1 hiện có mà không phát sinh SQL migration mới hay phá vỡ các test double của `IApplicationDbContext`.
- Mat:
  - Số lần in (`PrintCount`) được tổng hợp từ bảng `audit_logs` theo `(EntityType = "VISIT", EntityId, Action = "PRINT_TICKET")` thay vì thêm cột đếm riêng trên bảng `visits`.
- Rui ro con lai:
  - Khi một `Visit` hợp lệ được tạo thủ công mà chưa có dòng `queue_entries` loại `EXAMINATION`, hệ thống tự động tính số thứ tự fallback theo thứ tự `CheckedInAt` và `Id` của cùng buồng khám trong ngày để phiếu khám luôn có `TicketNo` nhất quán.
