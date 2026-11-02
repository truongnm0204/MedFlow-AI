# 0002 — Hợp đồng API và quy tắc tính khả dụng cho lịch khám (`available-slots` & `service-shifts`)

- Ngay: 2026-11-01
- Trang thai: accepted
- Lane: high-risk

## Context
Use case UC11 (View Available Appointment Slots — M1-46) cung cấp hai API cho bệnh nhân tra cứu ngày khám, ca khám và khung giờ (slot) còn khả dụng theo Bác sĩ (`GET /api/v1/appointments/available-slots`) hoặc theo Dịch vụ khám (`GET /api/v1/appointments/service-shifts`).

Frontend (#45) đã được merge trước và cố định cấu trúc hiển thị trong `medflow-ai-frontend/src/features/appointment/types/slot-booking.types.ts` (không dùng envelope, tự động fallback sang dữ liệu mock nếu API lỗi hoặc sai shape). Ngoài ra:
- Các cột thời gian `starts_at`, `ends_at` trong bảng `examination_slots` và `booked_at` trong `appointments` đều thuộc kiểu `timestamp without time zone` (naive timestamp).
- Dữ liệu seed gốc `003` chỉ chứa các slot cố định của tháng 10/2026, nên khi chạy demo ở thời điểm hiện tại hoặc tương lai sẽ không có slot `OPEN` trong dải 7 ngày tới.

## Decision
1. **Giữ nguyên hợp đồng hiển thị theo Frontend (`slot-booking.types.ts`)**:
   - Hai endpoint trả trực tiếp `AvailableSlotsResponse` và `ServiceShiftsResponse` qua `Ok(...)` (camelCase mặc định của ASP.NET Core).
   - Mọi nhãn ngày tiếng Việt (`Hôm nay`, `Ngày mai`, `Thứ 2`..`Chủ Nhật`), badge trạng thái (`AVAILABLE`, `FEW_SLOTS`, `LAST_SLOT`, `FULL`, `OFF`), tóm tắt ca trực bác sĩ (`ActiveDayShift`) và thông tin hai ca sáng/chiều (`shift-morning`, `shift-afternoon`) được định dạng ngay tại `AppointmentSlotService`.
   - Khi `dateIso` được truyền vào hợp lệ định dạng `yyyy-MM-dd` nhưng nằm ngoài dải 7 ngày (`today .. today+6`), hệ thống không trả lỗi 400 mà tự động chọn ngày khả dụng đầu tiên trong dải 7 ngày (hoặc `today` nếu cả tuần đều nghỉ/hết chỗ), tương thích với giá trị mặc định `'2026-10-13'` của store trên FE.
2. **Quy ước múi giờ Việt Nam (UTC+7) cho timestamp naive**:
   - Mọi mốc `starts_at` / `ends_at` trong DB được hiểu là giờ địa phương Việt Nam (`UTC+7`, không có DST).
   - `AppointmentSlotService` nhận `TimeProvider` qua DI và quy đổi `GetUtcNow().UtcDateTime.AddHours(7)` sang `DateTimeKind.Unspecified` để tính `today`, kiểm tra `BookingLeadHours` và `BookingWindowDays` từ `IAppointmentPolicyService.GetEffectiveAsync(hospitalId)`.
3. **Quy tắc tính chỗ trống (Remaining Capacity)**:
   - Một slot chỉ được đưa vào kết quả khi `status = 'OPEN'`, nằm trong dải 7 ngày tính từ `today`, `starts_at >= nowLocal + BookingLeadHours` và `DateOnly(starts_at) <= today + BookingWindowDays`.
   - Mọi bản ghi `appointments` gắn với slot có `status IS NULL OR UPPER(status) <> 'CANCELLED'` đều được tính là đang chiếm 1 chỗ (`remaining = max(0, capacity - booked)`), đảm bảo an toàn không overbook.
4. **Phân quyền hai lớp (Two-layer authorization) và danh sách hồ sơ bệnh nhân (`profiles`)**:
   - Layer 1: `[Authorize(Roles = "PATIENT")]` trên `AppointmentsController`.
   - Layer 2: `AppointmentSlotService` trích xuất `userId` từ JWT claims (`NameIdentifier` / `sub`), kiểm tra tài khoản `User` (`ACTIVE`) và hồ sơ `Patient` (`ACTIVE`) của chính người dùng đó, sau đó nạp thêm các hồ sơ người thân trong `patient_family_links` thỏa `owner_patient_id = self.Id`, `verification_status = 'VERIFIED'`, `revoked_at IS NULL`, `(valid_until IS NULL OR valid_until > nowLocal)` và `member.status = 'ACTIVE'`. Không nhận `patientId` từ query.
5. **Migration seed `006_seed_upcoming_examination_slots.sql`**:
   - Bổ sung migration SQL idempotent (`NOT EXISTS`) tạo các slot `OPEN` (8 slot sáng `07:30–11:30`, 6 slot chiều `13:30–16:30`, `capacity = 3`) cho 14 ngày kế tiếp (`CURRENT_DATE + 1 .. CURRENT_DATE + 14`, trừ Chủ Nhật) đối với 2 cặp `(service_id, doctor_id, room_id)` = `(1, 1, 1)` và `(2, 2, 3)`.

## Consequences
- Duoc:
  - Frontend kết nối hiển thị dữ liệu thật từ DB cho cả luồng đặt khám theo Bác sĩ lẫn theo Dịch vụ mà không cần sửa bất kỳ dòng code FE nào.
  - Inject `TimeProvider` giúp kiểm thử đơn vị (unit test) cố định thời gian dễ dàng, kiểm chứng chính xác `BookingLeadHours`, `BookingWindowDays` và nhãn ngày tiếng Việt.
  - Migration `006` có thể chạy lặp lại an toàn bất cứ lúc nào để làm mới dữ liệu demo 14 ngày tới mà không ảnh hưởng các integration test hiện hữu.
- Mat:
  - DTO phản hồi của API mang nhiều trường hiển thị UI (badge, label tiếng Việt) thay vì chỉ trả dữ liệu thô; đây là đánh đổi có chủ đích để khớp hợp đồng FE đã đóng băng.
- Rui ro con lai:
  - Nếu về sau bổ sung thêm trạng thái appointment không chiếm chỗ ngoài `CANCELLED`, cần cập nhật điều kiện lọc tại truy vấn đếm số lượng đặt chỗ trong `AppointmentSlotService`.
