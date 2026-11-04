# 0008 — Hợp đồng API Bảng điều khiển Bác sĩ (`GET /api/v1/doctor/dashboard/summary`)

- Ngay: 2026-11-04
- Trang thai: accepted
- Lane: high-risk

## Context
Issue `#14` (`M1-014 — [M1][BE] View Doctor Dashboard`) yêu cầu cung cấp API và quy tắc nghiệp vụ cho màn hình khởi đầu sau đăng nhập của vai trò **Doctor** (`DOCTOR`), chỉ hiển thị dữ liệu thuộc phạm vi phụ trách của bác sĩ đang đăng nhập và chỉ cung cấp các lối vào chức năng mà vai trò `DOCTOR` được phép sử dụng (UC30–UC38).

Trên nhánh `develop`, màn hình Frontend `DoctorDashboardPage` (`#13`, `/doctor`) đang đọc trực tiếp dữ liệu tĩnh từ `doctor-mock-data.ts`, trong khi các bảng dữ liệu nền của schema v4.1 (`users`, `roles`, `user_roles`, `permissions`, `role_permissions`, `staff`, `doctors`, `hospitals`, `departments`, `hospital_rooms`, `specialties`, `services`, `examination_slots`, `appointments`, `visits`, `queue_entries`) đã có sẵn trong PostgreSQL (`database/medflow_schema_v4_1.sql` và `003_v41_priority1_seed.sql`). Một số cột có sẵn trong bảng `visits` (`visit_code`, `assigned_doctor_id`, `examination_room_id`, `checked_in_by_staff_id`, `completed_by_doctor_id`) và `queue_entries` (`visit_id`, `service_point_id`, `current_service_point_id`, `queue_date`, `status`, `scheduled_at`, `presence_status`, `arrived_at`, `arrival_class`, `manual_priority`, `ready_at`, `completed_at`, `blocked_reason`) chưa được ánh xạ đầy đủ vào thực thể EF `Visit` và `QueueEntry`.

## Decision
1. **Hợp đồng HTTP (`GET /api/v1/doctor/dashboard/summary` và các route đồng danh)**:
   - Controller: `DoctorDashboardController` hỗ trợ:
     - `GET /api/v1/doctor/dashboard/summary` (canonical)
     - `GET /api/v1/doctor/dashboard`
     - `GET /api/v1/doctors/me/dashboard`
     - `GET /api/v1/clinical/doctor-dashboard`
   - Query parameters (`GetDoctorDashboardRequest`):
     - `QueueLimit` (`int`, mặc định `20`, hợp lệ `1..50`)
     - `DiagnosticLimit` (`int`, mặc định `10`, hợp lệ `1..50`)
     - `Status` (`string?`, tùy chọn: `ALL | WAITING | CALLED | IN_CONSULTATION | AWAITING_RESULTS | COMPLETED`)
     - `Priority` (`string?`, tùy chọn: `ALL | NORMAL | URGENT | ELDERLY | RETURN_RESULT`)
     - `Keyword` (`string?`, tùy chọn: tìm theo tên bệnh nhân, mã bệnh nhân, số thứ tự, lý do khám)
   - Response `200 OK` (`DoctorDashboardResponseDto`):
     - `facilityName`, `facilityCode`, `roomCode`, `roomName`, `departmentName`, `shiftLabel`
     - `stats` (`DoctorStatsDto`: `assignedWaiting`, `inConsultation`, `pendingLabResults`, `completedToday`)
     - `assignedQueue` (`IReadOnlyList<AssignedPatientEntryDto>`: `id`, `queueNo`, `patientName`, `patientCode`, `age`, `gender`, `chiefComplaint`, `assignedAt`, `priority`, `status`)
     - `pendingDiagnostics` (`IReadOnlyList<PendingDiagnosticItemDto>`: `id`, `orderCode`, `patientName`, `patientCode`, `serviceName`, `orderedAt`, `status`)
     - `quickActions` (`IReadOnlyList<DoctorQuickActionDto>`: `id`, `label`, `description`, `ucCode`, `shortcut`, `route`, `icon`, `accent`)
     - `doctorProfile` (`DoctorDashboardProfileDto`: `userId`, `doctorId`, `doctorCode`, `staffCode`, `email`, `fullName`, `professionalTitle`, `displayName`, `specialtyName`, `departmentId`, `departmentName`, `hospitalId`, `hospitalCode`, `hospitalName`, `roomCode`, `roomName`, `licenseNumber`, `roles`, `permissions`)
     - `generatedAtUtc` (`DateTime`)

2. **Phân quyền 2 lớp (Two-Layer Authorization) và Thứ tự kiểm tra lỗi (`ProblemDetails`)**:
   - **Layer 1**: `[Authorize(Roles = "DOCTOR")]` trên `DoctorDashboardController`.
     - Chưa đăng nhập / thiếu Bearer token → `401 Unauthorized`.
     - Đăng nhập bằng JWT khác vai trò `DOCTOR` (`PATIENT`, `RECEPTION`, `DIAGNOSTIC_STAFF`, `PHARMACY_STAFF`, `HOSPITAL_ADMIN`, `SYSTEM_ADMIN`) → `403 Forbidden`.
   - **Layer 2 (`DoctorDashboardService.GetDashboardAsync`)**:
     - `QueueLimit` hoặc `DiagnosticLimit` ngoài khoảng `1..50`, hoặc `Status`/`Priority` không thuộc tập hợp lệ → `400 Bad Request` (`ValidationException`).
     - Không tìm thấy `User` theo `actorUserId` → `404 Not Found` (`NotFoundException`).
     - `User.AccountStatus != ACTIVE` hoặc đang bị khóa (`LockedUntil > nowUtc`) → `403 Forbidden` (`ForbiddenException`).
     - Bản ghi `user_roles` trong DB của `actorUserId` không chứa role `DOCTOR` (chống giả mạo hoặc token cũ sau khi thu hồi quyền) → `403 Forbidden` (`ForbiddenException`).
     - Không có hồ sơ `Staff` gắn với `actorUserId`, `Staff.Status != "ACTIVE"`, hoặc `Staff.HospitalId == null` → `403 Forbidden` (`ForbiddenException`).
     - Không có hồ sơ `Doctor` gắn với `Staff.Id` hoặc `!Doctor.IsActive` → `403 Forbidden` (`ForbiddenException`).
     - Không tìm thấy `Hospital` theo `HospitalId` → `404 Not Found` (`NotFoundException`); `Hospital.IsActive == false` → `403 Forbidden` (`ForbiddenException`).
   - **Cô lập dữ liệu sở hữu (Data Ownership)**:
     - Chỉ truy vấn các `Visit` có `AssignedDoctorId == doctor.Id` (hoặc `CompletedByDoctorId == doctor.Id`) và các `Appointment` thuộc `ExaminationSlot` có `DoctorId == doctor.Id` trong cùng `HospitalId`. Tuyệt đối không trả dữ liệu bệnh nhân của bác sĩ khác hoặc cơ sở y tế khác.
     - Không trả dữ liệu định danh nhạy cảm (`identification_hash`, `phone`, `address`, `email` của bệnh nhân) trong danh sách hàng đợi.

3. **Ánh xạ cột có sẵn trong Schema v4.1 (Không tạo SQL Migration mới)**:
   - Bổ sung ánh xạ các cột đã tồn tại trong `database/medflow_schema_v4_1.sql` vào `Visit` (`VisitCode`, `AssignedDoctorId`, `ExaminationRoomId`, `CheckedInByStaffId`, `CompletedByDoctorId`) và `QueueEntry` (`VisitId`, `ServicePointId`, `CurrentServicePointId`, `QueueDate`, `Status`, `ScheduledAt`, `PresenceStatus`, `ArrivedAt`, `ArrivalClass`, `ManualPriority`, `ReadyAt`, `CompletedAt`, `BlockedReason`).
   - Không thay đổi chữ ký `IApplicationDbContext` để tránh ảnh hưởng tới các test double hiện có trong `MedFlow.UnitTests`.

4. **Lọc lối vào chức năng (`QuickActions`) theo RBAC**:
   - Chỉ trả về các lối vào thuộc phạm vi Bác sĩ lâm sàng dựa trên `role_permissions` của `DOCTOR`:
     - `call-next` (`UC30`, `F1`) — khi có quyền `MEDICAL_RECORD_WRITE` hoặc `QUEUE_MANAGE`.
     - `open-consultation` (`UC33`, `F2`) — khi có quyền `MEDICAL_RECORD_WRITE`.
     - `create-diagnostic` (`UC34`, `F3`) — khi có quyền `DIAGNOSTIC_ORDER`.
     - `create-prescription` (`UC37`, `F4`) — khi có quyền `PRESCRIPTION_ISSUE`.
   - Không bao giờ trả về chức năng của Tiếp đón (`PATIENT_CHECKIN`), Nhà thuốc (`MEDICINE_DISPENSE`), Quản trị bệnh viện (`HOSPITAL_CONFIG`) hay Quản trị hệ thống (`USER_MANAGE`, `AUDIT_READ`).

## Consequences
- Duoc:
  - Đồng nhất 100% kiến trúc và quy ước với `AdminHospitalDashboardController` (`#20`) và `AdminSystemDashboardController` (`#22`).
  - Bảo vệ chặt chẽ bằng 2-layer authz ở cả Backend và Frontend (`<RequireDoctor>`), không cần tạo migration SQL mới và không phá vỡ bất kỳ unit test nào hiện có.
- Mat:
  - Dữ liệu `pendingDiagnostics` được tổng hợp từ các lượt `QueueEntries` có `QueueType == "DIAGNOSTIC"` gắn với `Visit` của bác sĩ và danh mục dịch vụ `DIAGNOSTIC` được cấp quyền (`DoctorServiceAuthorizations`) thay vì thêm `DbSet<DiagnosticOrder>` mới vào `IApplicationDbContext` (sẽ mở rộng ở M3 khi triển khai UC34/UC35).
- Rui ro con lai:
  - Khi DB chỉ có dữ liệu seed tĩnh ở các ngày mẫu (`2026-10-15..2026-10-28`), nếu lọc cứng theo ngày hiện tại sẽ trả về danh sách rỗng; giải quyết bằng cách ưu tiên ca trong ngày hiện tại (`today`) và fallback sang các `Visit`/`Appointment` được phân công cho bác sĩ trong DB (tương tự `HospitalAdminDashboardService`).
