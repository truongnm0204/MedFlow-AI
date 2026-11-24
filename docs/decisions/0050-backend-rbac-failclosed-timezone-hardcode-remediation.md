# 0050 — Backend Security, Fail-Closed RBAC, Hospital Timezone & Domain Hardcode Remediation

- Ngày: 2026-11-24
- Trạng thái: accepted
- Lane: high-risk
- Liên quan: `0001`, `0008`, `0013`, `0024`, `0027`, `0031`, `0038`, `0046`, `0047`, `0049`

## Bối cảnh

Sau PR #810 (`ADR-0049`), rà soát toàn diện tầng Backend (`Issue #811` / `TASK-0008`) ghi nhận bốn nhóm rủi ro bảo mật và sai lệch nghiệp vụ do giá trị gắn cứng (hardcode) và cơ chế dự phòng mở (fail-open):

1. **Cấu hình CORS & Môi trường**: `Program.cs` gắn cứng duy nhất origin `http://localhost:5173`; `appsettings.Development.json` chứa tài khoản email cá nhân trong `Smtp:Username`.
2. **Phân quyền RBAC Fail-Open**:
   - 12 service Lâm sàng, Cận lâm sàng và Dashboard có mảng quyền dự phòng `Fallback*Permissions` tự cấp đủ quyền khi bảng `role_permissions` rỗng.
   - 5 service Dược và Tiền sử bệnh bỏ qua kiểm tra quyền khi bảng `role_permissions` trống.
   - 10 service Báo cáo, Tiếp đón và Hàng đợi lọc `role_permissions` theo vai trò trước rồi mới kiểm tra `if (rolePermissionIds.Count > 0)` — dẫn tới khi quản trị viên thu hồi toàn bộ quyền của vai trò thì `Count == 0` khiến hệ thống bỏ qua bước chặn `403`.
   - 2 service Quản trị danh mục (`ExaminationServiceAdminService`, `ExaminationRoomService`) có nhánh tự gán `isSystemAdmin = true` hoặc bỏ qua kiểm tra `UserRoles` khi danh sách rỗng.
   - 5 Dashboard chưa chặn `403` khi vai trò có `0` quyền hợp lệ và chưa lọc dữ liệu thống kê/danh sách khi tài khoản chỉ được cấp một phần quyền.
3. **Xác minh định danh & Dữ liệu danh mục giả lập**:
   - `CreateWalkInVisitService` và `ConfirmDispenseRequest` mặc định `IsIdentityVerified = true` khi client không gửi trường xác nhận.
   - `DoctorCatalogService` gắn cứng bằng cấp `"Đại học Y Hà Nội"`, lịch trực tuần cố định và phòng `"Phòng 101 - Tầng 1, Khu Demo"`; `ExaminationServiceCatalogService` fallback về `"Khu Demo"`; `ConsultationWorkspaceService` ghi `"DEMO-ICD10"` khi bảng `icd10_codes` trống; `PrepareMedicinesService` sinh kệ thuốc giả theo phép chia dư `% 4`; `PharmacyDashboardService` tự đệm cảnh báo `LOW_STOCK` giả từ các lô thuốc tồn kho an toàn (`<= 170`).
4. **Độ lệch Múi giờ, Ca làm việc & `HospitalId ?? 1`**:
   - Xử lý ngày giờ phân tán giữa `_timeProvider.GetLocalNow()` (phụ thuộc OS container), `.AddHours(7)` thủ công (gây cộng dồn `+7h` trên cột PostgreSQL `timestamp without time zone` vốn đã lưu giờ địa phương), và `DateOnly.FromDateTime(DateTime.UtcNow)` (lệch ngày trong khung `00:00–06:59` giờ Việt Nam).
   - Khung giờ ca làm việc lệch nhau giữa các module và chưa tách biệt logic phân nhóm ca với logic kiểm tra giờ phục vụ thực tế.
   - Nhiều service fallback ngầm `HospitalId ?? 1` và đặt tên giả `"MedFlow Cơ sở {id}"`, tiềm ẩn nguy cơ ghi/đọc chéo dữ liệu giữa các bệnh viện.

## Quyết định

### 1. Cấu hình CORS, SMTP & `IOptions` có kiểm tra lúc khởi động (`ValidateOnStart`)

- **CORS (`Cors:AllowedOrigins`)**: Đọc danh sách origin từ cấu hình `Cors:AllowedOrigins`; chỉ fallback về `["http://localhost:5173", "https://localhost:5173"]` khi `Environment.IsDevelopment()` và cấu hình trống. Ở các môi trường khác, nếu không cấu hình origin thì không mở quyền CORS mặc định.
- **SMTP**: Gỡ tài khoản cá nhân khỏi `appsettings.Development.json`; thông tin xác thực SMTP thực tế chỉ cung cấp qua `dotnet user-secrets` hoặc biến môi trường.
- **Options chuẩn hóa**:
  - `AuthSecurityOptions` (section `Auth`): quản lý `OtpTtlMinutes` (`5`), `OtpResendThrottleSeconds` (`45`), `OtpMaxAttempts` (`5`), `MaxLoginAttempts` (`5`), `LockoutDurationMinutes` (`15` — khớp `docs/system-architecture.md`, cho phép cấu hình môi trường nâng lên `30` phút theo SRS UC02 mà không sửa code), và `IdentityClaimTtlDays` (`7`), kèm `ValidateOnStart()`.
  - `QrPaymentOptions` (section `QrPayment`): quản lý `PendingExpiryMinutes` (`15`) kèm `ValidateOnStart()`.
  - Hằng số vai trò và mã quyền tập trung tại `MedFlow.Domain.Constants.RoleNames` và `PermissionCodes`; hợp nhất `QueuePolicyKeys` về một định nghĩa duy nhất.

### 2. Khóa chặt RBAC Fail-Closed trên toàn bộ 29 Services & Lọc dữ liệu 5 Dashboards

- **Nguyên tắc Fail-Closed tuyệt đối**: Loại bỏ toàn bộ mảng `Fallback*Permissions`, nhánh `if (rolePermissionsConfigured)` và `if (rolePermissionIds.Count > 0)`. Khi bảng `role_permissions` trống hoặc vai trò bị thu hồi hết quyền cần thiết → luôn trả `403 Forbidden` (`ForbiddenException`).
- **Phân tách quyền Quản trị Hệ thống và Lâm sàng**:
  - `SYSTEM_ADMIN` quản trị hệ thống, tài khoản, phân quyền và cấu hình tích hợp; **tuyệt đối không có quyền mặc định trên các nghiệp vụ lâm sàng, hồ sơ bệnh án, kê đơn, cận lâm sàng hay cấp phát thuốc** (`ConsultationWorkspace`, `MedicalHistory`, `DoctorDashboard`, `Pharmacy*`, `Diagnostic*` đều từ chối `403`).
  - Ngay trên các endpoint vận hành/báo cáo/danh mục/nhân sự cho phép `SYSTEM_ADMIN` hoặc `HOSPITAL_ADMIN`, tài khoản vẫn bắt buộc phải có quyền quản trị hợp lệ trong `role_permissions` (`HOSPITAL_CONFIG` / `USER_MANAGE` / `AUDIT_READ` / `PAYMENT_VIEW`), không bỏ qua kiểm tra khi bảng quyền rỗng và không tồn tại bất kỳ nhánh `actorUserId == null => isSystemAdmin = true` nào trong tầng Application.
  - Ngoại lệ khôi phục kiểm soát duy nhất được thiết kế có chủ đích: `RoleCatalogService` (`/api/v1/admin/roles`) kiểm tra trực tiếp vai trò `SYSTEM_ADMIN` trong DB (đồng thời khóa bất biến không cho gỡ `USER_MANAGE` khỏi `SYSTEM_ADMIN`) để đóng vai trò kênh khôi phục/cấu hình lại `role_permissions` khi cần thiết.
- **5 Dashboards (`Doctor`, `Pharmacy`, `Diagnostic`, `HospitalAdmin`, `SystemAdmin`)**:
  - Trả `403 Forbidden` nếu tài khoản không có bất kỳ quyền hợp lệ nào của Dashboard tương ứng.
  - Khi tài khoản chỉ có một phần quyền, lọc đồng bộ cả `Stats`/`Metrics`, danh sách dữ liệu (`RecentAuditLogs`, `RoleSummaries`, `Worklist`, `PolicyAlerts`, `AssignedQueue`, `PendingDiagnostics`, `PendingResults`, `PrescriptionQueue`, `FefoBatchAlerts`), `QuickActions` và `AllowedModules` theo đúng tập quyền được cấp.

### 3. Toàn vẹn Xác minh Danh tính & Danh mục Lâm sàng / Dược

- **`IsIdentityVerified` mặc định `false`**:
  - `CreateWalkInVisitService` và `ConfirmDispenseRequest` mặc định `false` nếu client không gửi `isIdentityVerified`; `GetDispenseContextAsync` chỉ trả `IsIdentityVerified = true` khi đơn thuốc đã hoàn tất xuất kho trước đó.
  - Khách khám Walk-in bắt buộc có hồ sơ `Patient` và xác nhận đối chiếu giấy tờ tùy thân tại quầy, **không bắt buộc tạo tài khoản đăng nhập `User`** (`patients.user_id` vẫn cho phép `NULL`).
- **Không suy diễn hoặc bịa dữ liệu danh mục**:
  - `ConsultationWorkspaceService`: loại bỏ hoàn toàn nhánh bypass `"DEMO-ICD10"` khi bảng `icd10_codes` trống.
  - `DoctorCatalogService`: gỡ bằng cấp giả `"Đại học Y Hà Nội"`, không suy diễn nơi đào tạo từ tên khoa/chuyên khoa, gỡ phòng mẫu `"Phòng 101 - Tầng 1, Khu Demo"`, và tổng hợp `WeeklySchedule` trực tiếp từ các `ExaminationSlot` `OPEN` thực tế trong DB.
  - `ExaminationServiceCatalogService`: gỡ fallback `"Khu Demo"`, trả `"Chưa cập nhật"` khi khoa chưa có phòng hoạt động.
  - `PrepareMedicinesService` & `PharmacyDashboardService`: trả `"Chưa cập nhật"` cho vị trí kệ thuốc và chỉ phát cảnh báo tồn kho/FEFO khi lô thuốc thực sự vi phạm ngưỡng (`EXPIRED_LOCKED`, `QuantityOnHand <= 50`, hạn dùng `<= 180` ngày).

### 4. Đồng hồ Múi giờ Bệnh viện (`HospitalClock`), Phân ca (`HospitalShiftRules`) & Cô lập `HospitalId`

- **`HospitalClock` (`Asia/Ho_Chi_Minh` qua `TimeProvider`)**:
  - Mọi phép lấy giờ/ngày bệnh viện đi qua `HospitalClock.GetNow(timeProvider)` và `HospitalClock.GetToday(timeProvider)` thay cho `_timeProvider.GetLocalNow()`, `.AddHours(7)` rời rạc hay `DateOnly.FromDateTime(DateTime.UtcNow)`.
  - Phân biệt rõ cột lưu UTC (`audit_logs.occurred_at`, `users.locked_until`) với cột `timestamp without time zone` lưu giờ địa phương bệnh viện (`visits.checked_in_at`, `examination_slots.starts_at`, `appointments.booked_at`) thông qua `HospitalClock.EnsureHospitalLocal` để tránh lỗi cộng dồn `+7` giờ.
- **`HospitalShiftRules`**:
  - Chuẩn hóa khung giờ phục vụ mặc định: **Ca sáng `07:30–11:30`**, **Ca chiều `13:30–16:30`**, và **Ca trực tối Cận lâm sàng `17:00–21:00`**.
  - Tách biệt rõ `ResolveShift` (phân nhóm lịch/hàng đợi/nhãn ca) và `IsWithinWorkingHours` (kiểm tra thời điểm có thực sự nằm trong khung giờ phục vụ hay đang ở khoảng nghỉ giữa ca `11:30–13:30` / chuyển ca `16:30–17:00` / ngoài giờ).
  - Bất biến nghiệp vụ: `ExaminationSlots` trong DB luôn là nguồn dữ liệu chính cho lịch đặt khám; `HospitalShiftRules` không bao giờ dùng để tự động hủy lượt khám hay chặn bác sĩ/nhân viên CLS hoàn tất ca đang xử lý sau giờ kết thúc ca.
- **Loại bỏ hoàn toàn `HospitalId ?? 1` và `"MedFlow Cơ sở 1"`**:
  - Nhân viên bệnh viện thiếu `Staff.HospitalId` hợp lệ hoặc truy cập/ghi sang bệnh viện khác → `403 Forbidden` (không cho phép `request.HospitalId` ghi đè phạm vi bệnh viện của nhân viên).
  - `SYSTEM_ADMIN` tạo tài nguyên cấp bệnh viện nhưng thiếu `HospitalId` → `400 Bad Request`; `HospitalId` không tồn tại → `404 NotFound`.
  - Tên cơ sở y tế luôn truy vấn từ `Hospitals.Name` (fallback `"Chưa cập nhật"` khi thiếu dữ liệu).

## Hệ quả

- **Tích cực**: Đóng toàn bộ lỗ hổng leo thang/vượt rào phân quyền khi thu hồi quyền RBAC, ngăn rò rỉ dữ liệu chéo bệnh viện, chấm dứt sai lệch ngày giờ giữa máy dev Windows và container Linux UTC, và loại bỏ hoàn toàn dữ liệu giả lập khỏi tầng API Backend mà không cần thay đổi schema DB đã commit.
- **Lưu ý tiếp nối (Follow-up)**: Ở tầng Frontend, các biểu mẫu tiếp đón/nhà thuốc cần đảm bảo hộp kiểm đối chiếu giấy tờ phản ánh thao tác xác nhận thực tế của nhân viên trước khi gửi `isIdentityVerified: true` (theo dõi ở task Frontend độc lập để giữ nguyên biên độ không xung đột với `#812` và `#813`).
