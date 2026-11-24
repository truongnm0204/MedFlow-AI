# 0054 — Hợp đồng API Nhật ký Kiểm toán (`UC60`) & Cấu hình Tích hợp Hệ thống (`UC59`)

- Ngày: 2026-11-25
- Trạng thái: accepted
- Lane: high-risk
- Liên quan: `0001`, `0041`, `0042`, `0046`, `0050`, `Issue #820` (`#241`–`#248`)

## Bối cảnh

Phân hệ Quản trị Hệ thống cần hoàn thiện hai nhóm chức năng cốt lõi thuộc Iteration 3:

1. **Tra cứu & Lọc Nhật ký Kiểm toán (`UC60` — `#245`, `#246`, `#247`, `#248`)**: Cho phép quản trị viên truy vết các sự kiện xác thực, phân quyền, cấu hình chính sách và vận hành trên bảng `audit_logs`, hỗ trợ tìm kiếm từ khóa, lọc theo khoảng ngày bệnh viện (`Asia/Ho_Chi_Minh`, UTC+7), hành động, loại thực thể, tác nhân, cơ sở y tế và xem chi tiết JSON metadata (`change_data`) mà không làm lộ khóa bí mật hoặc dữ liệu nhạy cảm.
2. **Quản lý Cấu hình Tích hợp (`UC59` — `#241`, `#242`, `#243`, `#244`)**: Cho phép xem trạng thái kết nối và cập nhật thông số cấu hình theo từng cơ sở y tế cho 3 dịch vụ bên ngoài (`VIETQR_SEPAY`, `SMTP_EMAIL`, `AI_PROVIDER`) trên nền bảng `hospital_policies` sẵn có (không thay đổi schema DB đã commit), đồng thời bảo vệ tuyệt đối các khóa bí mật runtime (`SePay:WebhookApiKey`, `Smtp:Password`, `Ai*:ApiKey`).

## Quyết định

### 1. Hợp đồng Tra cứu & Lọc Nhật ký Kiểm toán (`AdminAuditLogsController` / `AuditLogQueryService`)

- **Endpoints**:
  - `GET /api/v1/admin/audit-logs` (bí danh `GET /api/v1/system-admin/audit-logs`): Phân trang (`page >= 1`, `1 <= pageSize <= 100`), tìm kiếm `keyword`, lọc theo `action`, `entityType`, `actorUserId`, `hospitalId`, `fromDate`, `toDate`, kèm thẻ thống kê `metrics` và danh sách giá trị lọc hợp lệ trong phạm vi quyền (`availableActions`, `availableEntityTypes`).
  - `GET /api/v1/admin/audit-logs/{id:long}` (bí danh `GET /api/v1/system-admin/audit-logs/{id:long}`): Chi tiết bản ghi kiểm toán kèm `changeData` đã được làm sạch/che trường nhạy cảm.
- **Múi giờ Bệnh viện (`HospitalClock` UTC+7)**:
  - `audit_logs.occurred_at` lưu thời điểm UTC. Khi lọc theo `fromDate` / `toDate` (`DateOnly`, lịch bệnh viện `Asia/Ho_Chi_Minh`), hệ thống quy đổi ranh giới đầu ngày `[fromDate 00:00:00 +07:00)` và đầu ngày kế tiếp `[toDate + 1 day 00:00:00 +07:00)` sang UTC qua `HospitalClock.ConvertHospitalLocalToUtc` để không sót hoặc lệch sự kiện trong khung `00:00–06:59` giờ Việt Nam. Kiểm tra `fromDate <= toDate` và giới hạn khoảng tra cứu tối đa `366` ngày.
- **Phân quyền 2 lớp Fail-Closed & Cô lập Cơ sở**:
  - Lớp 1: `[Authorize(Roles = "SYSTEM_ADMIN,HOSPITAL_ADMIN")]`.
  - Lớp 2: Kiểm tra `users.status = ACTIVE`, không bị khóa tạm thời, hồ sơ `staff` (nếu có) đang `ACTIVE`, có vai trò `SYSTEM_ADMIN` hoặc `HOSPITAL_ADMIN` trong `user_roles` và bắt buộc có quyền `PermissionCodes.AuditRead` (`AUDIT_READ`) trong `role_permissions` (fail-closed khi bảng quyền trống hoặc vai trò chưa được cấp `AUDIT_READ`).
  - `SYSTEM_ADMIN` được xem toàn hệ thống hoặc lọc theo `hospitalId` bất kỳ; `HOSPITAL_ADMIN` bắt buộc có `staff.hospital_id` hợp lệ và chỉ được xem bản ghi có `audit_logs.hospital_id == staff.hospital_id` (truyền `hospitalId` khác cơ sở hoặc truy cập `id` thuộc cơ sở khác / cấp hệ thống `hospital_id IS NULL` đều trả `403 Forbidden`).
- **Che (Mask) dữ liệu nhạy cảm trong Metadata (`SensitiveDataMasker`)**:
  - Duyệt đệ quy cấu trúc JSON của `change_data` (kể cả chuỗi JSON lồng trong `OldValue` / `NewValue`) để thay thế giá trị của mọi khóa nhạy cảm (`password`, `passwordHash`, `secret`, `apiKey`, `webhookApiKey`, `token`, `accessToken`, `refreshToken`, `tokenHash`, `otp`, `otpCode`, `privateKey`, `clientSecret`, `authorization`, `hashKey`, `identificationNumber`, `cccd`, `cmnd`) bằng `"***REDACTED***"`, đồng thời che chuỗi `Bearer`/`Apikey` nếu xuất hiện.

### 2. Hợp đồng Cấu hình Tích hợp (`AdminIntegrationsController` / `IntegrationConfigService`)

- **Endpoints**:
  - `GET /api/v1/admin/integrations` (bí danh `GET /api/v1/system-admin/integrations`): Trả danh sách 3 tích hợp chuẩn của cơ sở mục tiêu (`VIETQR_SEPAY`, `SMTP_EMAIL`, `AI_PROVIDER`) kèm danh sách cơ sở khả dụng (`availableHospitals`).
  - `PUT /api/v1/admin/integrations/{providerKey}` (bí danh `PUT /api/v1/system-admin/integrations/{providerKey}`): Cập nhật cấu hình tích hợp theo cơ sở y tế.
- **Phân quyền 2 lớp Fail-Closed**:
  - Lớp 1: `[Authorize(Roles = "SYSTEM_ADMIN,HOSPITAL_ADMIN")]`.
  - Lớp 2: Xác minh tài khoản hoạt động, vai trò DB (`SYSTEM_ADMIN` hoặc `HOSPITAL_ADMIN`) và quyền `PermissionCodes.HospitalConfig` (`HOSPITAL_CONFIG`). `HOSPITAL_ADMIN` bị khóa cứng vào `staff.hospital_id` của mình (`hospitalId` khác trả `403 Forbidden`); `SYSTEM_ADMIN` có thể chọn `hospitalId` của cơ sở cần xem/cập nhật (thiếu `hospitalId` khi không có `staff.hospital_id` trả `400 Bad Request`; cơ sở không tồn tại trả `404 NotFound`).
- **Lưu trữ bất biến theo phiên bản trên `hospital_policies` & Tương thích `VietQrAccountPolicyReader`**:
  - Cấu hình tích hợp theo cơ sở lưu tại `hospital_policies` với `policy_type = 'INTEGRATION'` và `policy_key` tương ứng (`vietqr_sepay`, `smtp_email`, `ai_provider`) theo cơ chế đóng dòng hiệu lực cũ (`effective_to = nowUtc`) và thêm dòng mới (`policy_version = max + 1`, `effective_from = nowUtc`, `effective_to = null`).
  - Riêng `VIETQR_SEPAY`: đồng bộ đọc/ghi song hành với `policy_type = 'PAYMENT'`, `policy_key = 'vietqr_account'` (`bin` 6 chữ số, `accountNumber` 6–19 chữ số, `accountName` 1–50 ký tự) để luồng sinh mã QR thanh toán (`UC42` / `ADR-0041`) áp dụng tức thì cấu hình mới của cơ sở.
  - Mọi khóa bí mật runtime (`SePay:WebhookApiKey`, `Smtp:Password`, `AiRecommendation:ApiKey` / `Ai:ApiKey`) chỉ đọc trạng thái cấu hình từ `IConfiguration` (`hasSecretConfigured: bool`, `maskedSecret: "••••••••••••"` khi đã cấu hình hoặc `null` khi chưa cấu hình), **tuyệt đối không trả khóa thô về client và không lưu secret vào `hospital_policies`**.
  - Khi cấu hình thay đổi, ghi bản ghi `audit_logs` với `action = 'INTEGRATION_CONFIG_UPDATED'`, `entity_type = 'INTEGRATION_CONFIG'`, `hospital_id = targetHospitalId` trong cùng một giao dịch `SaveChangesAsync`.

## Hệ quả

- **Tích cực**: Khép kín trọn vẹn UC59 và UC60 trên cả Backend và Frontend mà không cần sửa schema DB hoặc các tệp dùng chung nhạy cảm xung đột (`IApplicationDbContext.cs`, `PermissionCodes.cs`), đảm bảo nguyên tắc fail-closed RBAC, cô lập dữ liệu đa cơ sở, chuẩn hóa múi giờ UTC+7 và bảo mật tuyệt đối khóa tích hợp.
- **Ràng buộc vận hành**: Khóa bí mật xác thực với nhà cung cấp bên ngoài tiếp tục được quản trị qua biến môi trường / secret store của hạ tầng triển khai; giao diện quản trị chỉ cho phép điều chỉnh các tham số nghiệp vụ và bật/tắt tích hợp theo cơ sở.
