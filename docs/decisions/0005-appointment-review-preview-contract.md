# 0005 — Hợp đồng API và quy tắc nghiệp vụ xem trước đặt lịch khám (`GET /api/v1/appointments/review-preview`)

- Ngay: 2026-11-01
- Trang thai: accepted
- Lane: high-risk

## Context
Issue #48 (M1-046 — Review Appointment Booking, bước soát xét trước khi xác nhận trong UC12) cung cấp endpoint `GET /api/v1/appointments/review-preview` để bệnh nhân (`PATIENT`) kiểm tra lại toàn bộ thông tin hồ sơ người bệnh, bác sĩ/dịch vụ, thời gian/phòng khám dự kiến, bảng kê chi phí dự tính và quy định tiếp đón trước khi bấm nút xác nhận đặt lịch (`POST /api/v1/appointments`).

Frontend (#47/#49) đã cố định hợp đồng tại `medflow-ai-frontend/src/features/appointment/api/review-booking.api.ts` và `types/review-booking.types.ts`:
- Request query `ReviewBookingQueryParams`: `{ mode, doctorId, serviceId, serviceCode, patientId, dateIso, slotId, shiftId, symptomNote }`.
- Response `AppointmentReviewPayload`: `{ patientInfo, scheduleLocation, feeSummary, receptionPolicy }`.
- FE chỉ fallback dữ liệu mẫu khi gặp `404` hoặc lỗi kết nối mạng; mọi lỗi nghiệp vụ khác hiển thị nguyên văn `ProblemDetails.detail` tiếng Việt.

## Decision
1. **Hợp đồng truy vấn & ánh xạ `slotId`/`shiftId` theo chế độ**:
   - Endpoint `GET /api/v1/appointments/review-preview` nhận `GetAppointmentReviewPreviewRequest` qua `[FromQuery]`.
   - FE luôn gửi đồng thời cả `slotId` và `shiftId` trên query string; backend ánh xạ sang `SlotOrShiftId` theo `mode` (`string.Equals(mode?.Trim(), "service", OrdinalIgnoreCase) ? request.ShiftId : request.SlotId`) trước khi chuyển vào luồng phân giải chung `ResolveSelectionAsync`.
2. **Tái sử dụng quy tắc nghiệp vụ của `POST /api/v1/appointments` (Decision 0003) ở chế độ chỉ đọc**:
   - Thực thi đầy đủ chuỗi kiểm tra của `0003`: kiểm tra đầu vào (`400`) → tra cứu danh mục dịch vụ/bác sĩ & phân quyền thực hiện dịch vụ (`404`/`400`) → phân quyền hai lớp (`[Authorize(Roles = "PATIENT")]` + kiểm tra ownership hồ sơ bản thân hoặc thành viên gia đình `VERIFIED` còn hiệu lực, vi phạm trả `403`) → phân giải slot theo bác sĩ hoặc chọn slot đầu tiên còn chỗ trong ca dịch vụ (`404`/`400`/`409`) → kiểm tra khả năng đặt theo `BookingLeadHours`/`BookingWindowDays` (`400`/`409`) → kiểm tra sức chứa `EnsureSlotHasCapacityAsync` (`409`) → kiểm tra giới hạn bệnh nhân `EnsurePatientLimitsAsync` gồm trùng lịch, số lịch tối đa và khóa do `NO_SHOW` (`409`).
   - **Read-only tuyệt đối**: không mở giao dịch (`BeginTransactionAsync`), không lấy khóa (`IAppointmentBookingLock`), không tạo bản ghi `appointments`, không ghi `audit_logs`, không gọi `SaveChangesAsync`.
3. **Nguồn dữ liệu ánh xạ phản hồi (`AppointmentReviewPreviewMapper`)**:

|Nhóm|Trường|Nguồn dữ liệu & Quy tắc định dạng|
|---|---|---|
|`patientInfo`|`patientId`, `patientCode`, `fullName`|Từ thực thể `Patient` đã xác thực quyền sở hữu (`Id` định dạng `InvariantCulture`)|
|`patientInfo`|`relationshipBadgeLabel`, `isPrimaryAccount`|Chính chủ: `"Chính chủ tài khoản"`, `true`; người thân: `$"Hồ sơ người thân • {FormatRelationshipLabel(relationship).Trim('(', ')')}"`, `false`|
|`patientInfo`|`dateOfBirth`, `ageLabel`, `gender`|`dd/MM/yyyy` (null → `"Chưa cập nhật"`), `$"({CalculateAge} tuổi)"` (null → `""`), `FormatGender` (`"Nam"`/`"Nữ"`)|
|`patientInfo`|`phone`|`patient.Phone` nếu có; nếu rỗng fallback sang `accountOwner.Phone`; nếu vẫn rỗng → `"Chưa cập nhật"`|
|`patientInfo`|`nationalId`|**Bảo vệ PII/PHI**: nếu `IdentificationHash` không rỗng trả hằng số che mặt `PatientProfileService.MaskedVerifiedIdentityText` (`"****** (Đã xác thực CCCD)"`), ngược lại `"Chưa xác thực CCCD"`. Tuyệt đối không trả chuỗi băm định danh|
|`patientInfo`|`currentAddress`, `symptomNote`|`patient.Address` trim (rỗng → `"Chưa cập nhật"`), `symptomNote ?? ""`|
|`scheduleLocation`|`mode`, `doctorId`, `doctorName`, `doctorAvatarUrl`|Chế độ `"doctor"` hoặc `"service"`; ở chế độ dịch vụ nếu slot có `DoctorId` thì nạp `DoctorDetailDto` tương ứng, nếu không có bác sĩ thì fallback `""` và `"Bác sĩ điều phối theo ca khám"`|
|`scheduleLocation`|`doctorRoleBadge`, `doctorCredentialsSubtitle`|Theo chế độ `"doctor"` / `"service"` và học hàm/học vị, chuyên khoa, số năm kinh nghiệm của bác sĩ|
|`scheduleLocation`|`specialtyName`, `serviceId`, `serviceCode`, `serviceName`, `serviceDisplayLabel`|Từ `ExaminationServiceDto`, `serviceDisplayLabel = $"{Name} (Mã {ServiceCode})"`|
|`scheduleLocation`|`dateIso`, `slotOrShiftId`, `scheduledSlotDisplay`, `checkInDeadlineTime`|`yyyy-MM-dd` từ `slot.StartsAt`; `slot.Id` (doctor) hoặc `"shift-morning"`/`"shift-afternoon"` (service); chuỗi giờ-thứ-ngày-ca; hạn check-in = `StartsAt - 15 phút`|
|`scheduleLocation`|`allocatedRoomTitle`, `shortRoomNumber`|Ưu tiên `HospitalRoom` của slot (`FormatHospitalRoom`/`ExtractRoomLabel`), fallback `doctor.Room`, `service.Location`, hoặc thông báo tại Quầy Tiếp đón|
|`scheduleLocation`|`durationBadgeLabel`, `durationFootnote`|Từ `service.DurationMinutes`|
|`feeSummary`|`serviceTypeBadge`, `items`, `totalAmount`, `totalDisclaimer`, `paymentMethodNote`|`price = service.Price > 0 ? service.Price : doctor?.Price ?? 0m`; gồm 2 dòng (`fee-main-consultation` = `price`, `fee-vitals-ehr` = `0` miễn phí trọn gói)|
|`receptionPolicy`|`badgeLabel`, `roomNumber`, `receptionCounterLabel`, `earlyArrivalMinutes`, `checkInDeadlineTime`, `lateGraceMinutes`|`earlyArrivalMinutes = 15` (hằng số khớp `AppointmentListService`), `lateGraceMinutes = policy.NoShowGraceMinutes`|

4. **Mã trạng thái HTTP & thông điệp lỗi**:
   - `200 OK`: trả `AppointmentReviewPreviewResponse`.
   - `400 Bad Request` / `401 Unauthorized` / `403 Forbidden` / `404 Not Found` / `409 Conflict`: trả `ProblemDetails` với thông điệp tiếng Việt đồng nhất hoàn toàn với `POST /api/v1/appointments`.

## Consequences
- Duoc:
  - Dùng chung `ResolveSelectionAsync`, `EnsureSlotHasCapacityAsync` và `EnsurePatientLimitsAsync` trong `AppointmentBookingService`, đảm bảo bệnh nhân được cảnh báo sớm mọi lỗi (hết chỗ, trùng giờ, vượt số lịch tối đa, tạm khóa do `NO_SHOW`, không thuộc quyền sở hữu) ngay ở bước xem trước mà không phát sinh lệch quy tắc với bước xác nhận.
  - Không làm lộ `IdentificationHash` và tuân thủ kiểm soát quyền sở hữu hai lớp đối với hồ sơ người thân.
- Mat:
  - Chế độ dịch vụ (`service`) tại bước xem trước chọn slot đầu tiên đang còn chỗ mà không giữ khóa; nếu slot đó đầy trước lúc bấm xác nhận, `POST /api/v1/appointments` sẽ tự động chọn slot kế tiếp trong ca hoặc trả `409` dưới khóa giao dịch.
- Rui ro con lai:
  - Không có rủi ro overbooking do bước `POST /api/v1/appointments` luôn kiểm tra lại toàn bộ điều kiện dưới khóa hàng (`FOR UPDATE`).
