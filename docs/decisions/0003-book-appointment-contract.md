# 0003 — Hợp đồng API và quy tắc nghiệp vụ đặt lịch khám (`POST /api/v1/appointments`)

- Ngay: 2026-11-01
- Trang thai: accepted
- Lane: high-risk

## Context
Use case UC12 (Book Appointment — M1-50) cung cấp API `POST /api/v1/appointments` để bệnh nhân (`PATIENT`) xác nhận đặt lịch khám cho bản thân hoặc cho hồ sơ người thân đã xác thực (`VERIFIED`).

Frontend (#49) đã cố định hợp đồng gọi API tại `medflow-ai-frontend/src/features/appointment/api/review-booking.api.ts` và `types/review-booking.types.ts`:
- Request `ConfirmBookingRequest`: `{ mode: 'doctor'|'service', patientId, doctorId, serviceId, serviceCode, dateIso 'YYYY-MM-DD', slotOrShiftId, symptomNote? (≤500) }`.
- Response `ConfirmBookingResponse`: `{ appointmentId, appointmentCode, queueNumber, status }`.
- Mọi lỗi khác 404 sẽ hiển thị nguyên văn trường `detail` của `ProblemDetails` lên banner cảnh báo người dùng, do đó toàn bộ thông điệp lỗi phải bằng tiếng Việt rõ ràng.
- Bảng `appointments` trong schema hiện tại chưa có cột lưu lý do khám (`symptomNote` từ FE), cần bổ sung `reason_for_visit` qua migration `007`.

## Decision
1. **Kiểm tra đầu vào (`ValidationException` → 400)** theo đúng thứ tự ưu tiên:
   - `Mode` (trim, không phân biệt hoa thường) phải thuộc `doctor` hoặc `service`; sai trả `"Hình thức đặt lịch không hợp lệ."`.
   - `PatientId` phải ép kiểu được sang `long > 0`; sai trả `"Vui lòng chọn hồ sơ người bệnh thăm khám."`.
   - `ServiceId` không được rỗng; sai trả `"Vui lòng chọn dịch vụ khám."`.
   - `DateIso` phải đúng định dạng `yyyy-MM-dd` (`InvariantCulture`), bắt buộc có mặt (không fallback như UC11); sai trả `"Ngày khám không hợp lệ (định dạng yyyy-MM-dd)."`.
   - `SymptomNote` sau khi trim có độ dài tối đa 500 ký tự (`"Lý do khám tối đa 500 ký tự."`); nếu rỗng sau khi trim thì lưu `null` vào `reason_for_visit`.
   - Chế độ `doctor`: `DoctorId` không được rỗng (`"Vui lòng chọn bác sĩ."`); `SlotOrShiftId` phải là `long > 0` (`"Khung giờ khám không hợp lệ."`).
   - Chế độ `service`: `SlotOrShiftId` phải là `shift-morning` hoặc `shift-afternoon` (không phân biệt hoa thường, sai trả `"Khung giờ khám không hợp lệ."`); bỏ qua `DoctorId` do FE gửi giá trị giả lập trong chế độ dịch vụ.
2. **Kiểm tra danh mục (Catalog checks)**:
   - Tra cứu dịch vụ qua `IExaminationServiceCatalogService.GetByIdOrCodeAsync(request.ServiceId)` (ném 404 nếu không tồn tại). Nếu `ServiceCode` có truyền và khác `serviceDto.ServiceCode` (`OrdinalIgnoreCase`) → 400 `"Mã dịch vụ khám không khớp với dịch vụ đã chọn."`. Nếu `!serviceDto.IsOnlineBookingActive` → 400 `"Dịch vụ khám này không hỗ trợ đặt lịch trực tuyến."`.
   - Với chế độ `doctor`: tra cứu bác sĩ qua `IDoctorCatalogService.GetByIdOrCodeAsync` (ném 404 nếu không tồn tại) và kiểm tra bản ghi `DoctorServiceAuthorizations` đang `IsActive == true`, nếu không có → 400 `"Bác sĩ không thực hiện dịch vụ khám này."`.
3. **Phân quyền hai lớp (Two-layer authorization & Ownership)**:
   - Layer 1: `[Authorize(Roles = "PATIENT")]` trên `AppointmentsController`.
   - Layer 2: Nạp hồ sơ bệnh nhân của người dùng hiện tại và các liên kết gia đình hợp lệ qua helper dùng chung `AppointmentBookingRules.LoadOwnedPatientsAsync` (tài khoản `User` `ACTIVE`, `Patient` `ACTIVE`, liên kết `VERIFIED`, chưa thu hồi `RevokedAt == null`, `ValidUntil == null || ValidUntil > nowLocal`, hồ sơ thành viên `ACTIVE`). Nếu `patientId` không phải hồ sơ bản thân hoặc thành viên hợp lệ → ném 403 `ForbiddenException("Bạn không có quyền đặt lịch khám cho hồ sơ không thuộc quyền quản lý.")` trước khi mở giao dịch hoặc ghi dữ liệu.
4. **Phân giải khung giờ khám (Slot resolution)**:
   - Chế độ `doctor`: tải `ExaminationSlots` theo `slotId`; không có → 404 `NotFoundException("ExaminationSlot", slotId)`. Nếu `slot.DoctorId != doctorId` hoặc `slot.ServiceId != serviceId` hoặc `DateOnly(slot.StartsAt) != dateIso` → 400 `"Khung giờ khám không khớp với bác sĩ, dịch vụ hoặc ngày đã chọn."`.
   - Chế độ `service`: lọc các slot của `serviceId` có `Status == "OPEN"`, `StartsAt`/`EndsAt != null`, đúng ngày `dateIso`, thuộc ca sáng (`StartsAt.TimeOfDay < 12:00`) hoặc chiều (`>= 12:00`), thỏa điều kiện thời gian quy tắc 5 và còn chỗ trống (`capacity - occupying > 0`). Sắp xếp theo `StartsAt`, sau đó `Id`, lấy slot đầu tiên; nếu không còn slot nào → 409 `ConflictException("Ca khám đã hết chỗ. Vui lòng chọn ca hoặc ngày khác.")`.
5. **Kiểm tra khả năng đặt của slot (Slot bookability)** sau khi khóa dòng, đối chiếu với giờ Việt Nam (`nowLocal`, UTC+7 naive) và cấu hình `IAppointmentPolicyService.GetEffectiveAsync(hospitalId)`:
   - `Status` khác `OPEN` hoặc thiếu `StartsAt`/`EndsAt` → 409 `"Khung giờ khám không còn mở để đặt lịch."`.
   - `StartsAt < nowLocal.AddHours(policy.BookingLeadHours)` → 400 `$"Chỉ được đặt lịch trước giờ khám tối thiểu {policy.BookingLeadHours} giờ."`.
   - `DateOnly(StartsAt) > today.AddDays(policy.BookingWindowDays)` → 400 `$"Chỉ được đặt lịch trong vòng {policy.BookingWindowDays} ngày tới."`.
6. **Kiểm tra sức chứa (Capacity)**:
   - Số chỗ đang chiếm là số `appointments` của slot có `Status == null || Status.ToUpper() != "CANCELLED"`. Nếu `occupying >= (slot.Capacity ?? 0)` → 409 `"Khung giờ khám vừa hết chỗ. Vui lòng chọn khung giờ khác."`.
7. **Giới hạn trên hồ sơ bệnh nhân (Patient limits)**:
   - Lịch khám đang hoạt động ("active booking") là bản ghi có `Status == null` hoặc `Status.ToUpper()` không thuộc `{"CANCELLED", "COMPLETED", "NO_SHOW", "CHECKED_IN"}`.
   - **Trùng giờ (Overlap)**: nếu bệnh nhân đã có lịch khám đang hoạt động giao với `[slot.StartsAt, slot.EndsAt)` (`other.StartsAt < slot.EndsAt && other.EndsAt > slot.StartsAt`) → 409 `"Hồ sơ này đã có lịch khám trùng thời gian."` (đồng thời chặn double-submit cùng slot).
   - **Số lịch tối đa (Max active)**: nếu số lịch đang hoạt động có `Slot.EndsAt > nowLocal` đạt `>= policy.MaxActiveBookings` → 409 `$"Hồ sơ này đã đạt tối đa {policy.MaxActiveBookings} lịch khám đang hoạt động."`.
   - **Khóa do vắng mặt (No-show block)**: khi `policy.NoShowBlockDays > 0`, đếm số lịch `Status.ToUpper() == "NO_SHOW"` có `Slot.StartsAt >= nowLocal.AddDays(-policy.NoShowLookbackDays)`. Nếu số lượng `>= policy.NoShowMaxCount` và `latestNoShowStartsAt.AddDays(policy.NoShowBlockDays) > nowLocal` → 409 `"Hồ sơ đang bị tạm khóa đặt lịch trực tuyến do vắng mặt nhiều lần."`.
8. **Lưu trữ, mã lịch hẹn, kiểm toán và số thứ tự (Persistence, Code, Audit & Queue Number)**:
   - Bản ghi `appointments` được lưu với `BookingChannel = "ONLINE"`, `Status = "BOOKED"`, `BookedByUserId = userId`, `BookedAt = nowLocal`, `ReasonForVisit = trimmedNoteOrNull`.
   - Sau lần `SaveChangesAsync` đầu tiên để sinh `Id`, gán `AppointmentCode = $"APT-{nowLocal:yyyyMM}-{Id:D4}"` và ghi log kiểm toán `IAuditLogger.Add(userId, "APPOINTMENT_BOOK", "APPOINTMENT", id.ToString(InvariantCulture), ...)` trước khi `SaveChangesAsync` lần hai và commit.
   - Số thứ tự `QueueNumber` được tính khi đọc (không lưu cột riêng): `rank` = tổng số `appointments` (mọi trạng thái, để không tái sử dụng số thứ tự đã hủy) có `Id <= appointment.Id`, cùng ngày dương lịch theo `Slot.StartsAt`, và cùng khóa nhóm (`DoctorId` nếu có, ngược lại `RoomId` nếu có, ngược lại `SlotId`), định dạng `rank.ToString("D3", InvariantCulture)`.
9. **Chiến lược khóa chống tranh chấp (Row-level lock strategy)**:
   - Giới thiệu cổng `IAppointmentBookingLock` tại tầng Application và hiện thực `AppointmentBookingLock` tại tầng Infrastructure (giữ cho Application không phụ thuộc `Microsoft.EntityFrameworkCore.Relational` và không làm gãy các fake `IApplicationDbContext` trong unit test).
   - Bên trong giao dịch mở bởi `IApplicationDbContext.BeginTransactionAsync`, thực thi tuần tự `SELECT 1 FROM ... FOR UPDATE` theo thứ tự cố định: `patients (patientId)` → `doctors (doctorId, nếu có)` → `examination_slots (slotId)`. Thứ tự khóa cố định loại bỏ nguy cơ deadlock đồng thời tuần tự hóa kiểm tra trùng lịch/giới hạn theo bệnh nhân, số thứ tự theo bác sĩ và sức chứa theo slot.

## Consequences
- Duoc:
  - Đảm bảo an toàn tuyệt đối trước lỗi overbooking, double-submit và race condition khi nhiều người dùng đặt cùng khung giờ hoặc cùng hồ sơ bệnh nhân đồng thời.
  - Phản hồi `201 Created` và các mã lỗi `400/401/403/404/409` khớp hoàn toàn với giao diện xác nhận đặt lịch của FE (#49).
  - Chia sẻ quy tắc nghiệp vụ cốt lõi giữa UC11 và UC12 qua `AppointmentBookingRules` mà không lặp mã nguồn.
- Mat:
  - Cần 2 lần gọi `SaveChangesAsync` trong cùng một giao dịch để sinh mã `APT-{yyyyMM}-{Id:D4}` dựa trên khóa chính tự tăng.
- Rui ro con lai:
  - Trang xác nhận trên FE hiện phụ thuộc thêm API `GET /appointments/review-preview` (#48) và `GET /appointments/{id}` (#54) để chạy trọn luồng giao diện không qua dữ liệu mock.
