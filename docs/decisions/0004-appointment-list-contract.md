# 0004 — Hợp đồng API và quy tắc xem danh sách lịch hẹn (`GET /api/v1/appointments/me`)

- Ngay: 2026-04-17
- Trang thai: accepted
- Lane: high-risk

## Context
Use case UC15 (Track Appointment Status — M1-52 / #52) cho phép bệnh nhân đã đăng nhập xem danh sách lịch hẹn sắp tới và lịch sử lịch hẹn của chính mình cùng các thành viên gia đình đã liên kết hợp lệ.

Frontend (#51) đã được merge trước và cố định hợp đồng trong `medflow-ai-frontend/src/features/appointment/types/appointment-list.types.ts` cùng `api/appointment-list.api.ts` (`GET /api/v1/appointments/me` với các tham số `memberId`, `tab`, `keyword`, `dateFrom`, `dateTo`, `page`, `pageSize`; chỉ fallback sang dữ liệu mock khi gặp 404 hoặc lỗi mạng). Ngoài ra:
- Trạng thái thực tế của một lịch hẹn phụ thuộc cả `appointments.status` lẫn `visits.status` (ví dụ lịch đã `CHECKED_IN` và ca khám `visits.status = 'COMPLETED'`).
- Số thứ tự khám và thông tin thanh toán nằm ở các bảng sẵn có `queue_entries` và `payments`, chưa được ánh xạ trong EF Core model.

## Decision
1. **Giữ nguyên hợp đồng hiển thị theo Frontend (`appointment-list.types.ts`) tại `GET /api/v1/appointments/me`**:
   - Trả trực tiếp `AppointmentListResponse` qua `Ok(...)` (JSON camelCase mặc định).
   - Tính toán `memberCounts` và `totalAllMembersCount` trên toàn bộ lịch hẹn hợp lệ thuộc phạm vi sở hữu của gia đình (bao gồm cả thành viên có `0` lịch hẹn), giữ thứ tự: hồ sơ bản thân (`"Bản thân"`) trước, tiếp theo là các thành viên gia đình theo `MemberPatientId` tăng dần.
   - Tính toán `tabCounts` (`all`, `upcoming`, `completed`, `cancelled`) sau khi áp bộ lọc `memberId`, `keyword`, `dateFrom`, `dateTo` nhưng trước khi lọc theo `tab`.
   - Sắp xếp tab `UPCOMING` theo `starts_at` tăng dần (hòa thì `id` tăng dần); các tab `ALL`, `COMPLETED`, `CANCELLED` theo `starts_at` giảm dần (hòa thì `id` giảm dần).
2. **Quy tắc tính trạng thái hiệu lực (Effective Status) và nhóm Tab**:
   - Nếu `appointments.status` là `CANCELLED` hoặc `NO_SHOW` → giữ nguyên.
   - Nếu bản ghi `visits` mới nhất của lịch hẹn có `status = 'COMPLETED'` hoặc `appointments.status = 'COMPLETED'` → `COMPLETED`.
   - Nếu `appointments.status` thuộc `CONFIRMED | BOOKED | PENDING | CHECKED_IN` → giữ nguyên.
   - Nếu `appointments.status` là `null` → `BOOKED`; các giá trị khác → `PENDING`.
   - Không tự suy luận `NO_SHOW` theo thời gian tại tầng đọc: lịch `BOOKED` đã qua giờ vẫn giữ trạng thái `BOOKED` cho tới khi use case xử lý vắng mặt ghi nhận.
   - Nhóm tab: `UPCOMING` (`CONFIRMED`, `BOOKED`, `PENDING`, `CHECKED_IN`), `COMPLETED` (`COMPLETED`), `CANCELLED` (`CANCELLED`, `NO_SHOW`).
3. **Phân quyền hai lớp (Two-layer authorization) và kiểm soát phạm vi sở hữu (`PatientAccessScope`)**:
   - Layer 1: `[Authorize(Roles = "PATIENT")]` trên `AppointmentsController`.
   - Layer 2: Tách helper dùng chung `PatientAccessScope` cho cả `AppointmentSlotService` và `AppointmentListService` để đảm bảo một nguồn chân lý duy nhất:
     - `LoadActiveSelfAsync`: nạp `Patient` kèm `User` theo `userId` từ JWT claims, bắt buộc `User.AccountStatus == ACTIVE` và `Patient.Status == ACTIVE`.
     - `WhereActiveMemberOf`: chỉ lấy `patient_family_links` của `self.Id` có `verification_status = 'VERIFIED'`, `revoked_at IS NULL`, `(valid_until IS NULL OR valid_until > nowLocal)` và `member.status = 'ACTIVE'`.
   - Nếu client truyền `memberId` khác `ALL`, bắt buộc đối chiếu thuộc tập scope của `userId`; nếu thuộc bệnh nhân khác hoặc liên kết đã thu hồi/hết hạn → ném `ForbiddenException` (`403 Forbidden`).
4. **Ánh xạ EF Core read-only cho `Visit`, `QueueEntry`, `Payment` không qua migration**:
   - Bổ sung 3 entity (`Visit`, `QueueEntry`, `Payment`) và 3 configuration (`VisitConfiguration`, `QueueEntryConfiguration`, `PaymentConfiguration`) ánh xạ trực tiếp vào các bảng sẵn có `visits`, `queue_entries`, `payments` trong schema v4.1.
   - Chỉ khai báo các cột phục vụ truy vấn đọc, không tạo navigation property sang các entity khác và không thay đổi schema SQL hay tạo migration mới.
5. **Cố ý lược bỏ các trường PHI nhạy cảm khỏi danh sách lịch hẹn**:
   - Các trường tùy chọn mang thông tin định danh/liên lạc chi tiết (`patientPhone`, `patientNationalId`, `patientAddress`, `patientBirthYearLabel`, `patientBirthDateLabel`) cùng các nhãn phụ không dùng trên màn danh sách (`serviceSubLabel`, `listedFeeServiceLabel`, `bookingTypeLabel`, `priorityBadgeLabel`) không được trả về ở endpoint danh sách; thông tin chi tiết sẽ do endpoint xem chi tiết lịch hẹn (#54) đảm nhận.

## Consequences
- Duoc:
  - Frontend màn danh sách lịch hẹn hiển thị dữ liệu thật từ PostgreSQL cho cả bệnh nhân và người thân liên kết mà không cần sửa code FE.
  - Quy tắc ownership layer 2 được gom về `PatientAccessScope`, tránh lệch logic giữa đặt lịch và xem lịch.
  - Không phát sinh migration hay thay đổi DDL, toàn bộ unit test và integration test hiện hữu giữ nguyên tính tương thích.
- Mat:
  - Lọc và phân trang thực hiện trong bộ nhớ sau khi nạp danh sách lịch hẹn của phạm vi gia đình; phù hợp vì số lượng lịch hẹn của một hộ gia đình nhỏ và cần toàn bộ tập dữ liệu để tính `memberCounts`.
- Rui ro con lai:
  - Khi triển khai các use case ghi nhận khám bệnh/thanh toán/hủy lịch về sau, nếu cần thêm cột hoặc quan hệ trên `Visit`, `QueueEntry`, `Payment` thì mở rộng thêm trên các entity và configuration đã tạo.
