# 0056 — SignalR Realtime Invalidation Contract, Two-Layer Group Authorization & Active Membership Revocation (`#831`)

- Ngày: 2026-11-25
- Trạng thái: accepted
- Lane: high-risk (Authentication / Two-Layer Authorization / PHI Protection / Realtime Transport)
- Liên quan: `0020`, `0032`, `0040`, `0041`, `0042`, `0045`, `0048`, `0055`, `0057`
- Issues: `#831`

## 1. Bối cảnh

Trong quy trình khám chữa bệnh ngoại trú của MedFlow AI, nhiều vai trò (`PATIENT`, `RECEPTION`, `DOCTOR`, `DIAGNOSTIC_STAFF`, `HOSPITAL_ADMIN`) cùng theo dõi biến động hàng đợi lâm sàng, hàng đợi cận lâm sàng và trạng thái đối soát thanh toán VietQR từ SePay Webhook (`#164` / ADR-0042). Trước `#831`, giao diện chỉ dựa trên cơ chế HTTP polling định kỳ, tạo độ trễ hiển thị khi bác sĩ bấm gọi số (`CallNext`), chuyển buồng khám hoặc khi ngân hàng xác nhận thanh toán.

Do kết nối WebSocket tồn tại lâu dài (long-lived connection) và chạm tới phạm vi theo dõi lượt khám của bệnh nhân, hồ sơ người thân gia đình (ADR-0045) cũng như phân tách đa bệnh viện, kênh thời gian thực phải tuân thủ nghiêm ngặt kiến trúc bảo mật hai lớp và nguyên tắc không truyền dữ liệu y tế nhạy cảm (Zero-PHI).

## 2. Quyết định Kiến trúc

### 2.1. Nguyên tắc Nguồn dữ liệu Chính thức duy nhất (Single Source of Truth)

- **PostgreSQL và REST API** vẫn là nguồn dữ liệu nghiệp vụ và kiểm tra phân quyền chính thức duy nhất.
- **ASP.NET Core SignalR (`/hubs/clinical`)** chỉ đóng vai trò kênh đẩy tín hiệu thông báo dữ liệu đã thay đổi (*invalidation signal*).
- Không đưa bất kỳ thao tác ghi nghiệp vụ nào (`CallNext`, `Check-in`, tạo mã VietQR, xác nhận thanh toán) vào Hub method.
- Trên giao diện thanh toán, trạng thái `PAID` **chỉ được hiển thị sau khi REST API (`GET /charges`) trả dữ liệu xác nhận chính thức**, tuyệt đối không gán trực tiếp `PAID` từ bản tin SignalR.

### 2.2. Xác thực JWT trên WebSocket, Giới hạn Tài nguyên & Chống Lộ Token

- Hub `ClinicalRealtimeHub` được ánh xạ tại `/hubs/clinical` với `.RequireAuthorization()` và `CloseOnAuthenticationExpiration = true` để tự động đóng kết nối ngay khi JWT hết hạn.
- Trong `JwtBearerOptions.Events.OnMessageReceived`, server **chỉ đọc query string `access_token` khi đường dẫn bắt đầu bằng `/hubs/clinical`** và chưa có header `Authorization`. Toàn bộ REST API `/api/v1/*` tiếp tục từ chối token qua query string.
- Kiểm tra header `Origin` (khi trình duyệt gửi kèm) đối chiếu danh sách `Cors:AllowedOrigins` hợp lệ ngay trong `OnConnectedAsync` nhằm chặn tấn công Cross-Site WebSocket Hijacking (CSWSH).
- Giới hạn tài nguyên trên mỗi kết nối (`RealtimeOptions`):
  - `EnableDetailedErrors = false` (không lộ chi tiết ngoại lệ nội bộ).
  - `MaximumReceiveMessageSize = 8192` bytes (`8 KB`).
  - Tối đa `20` nhóm (`MaxGroupsPerConnection = 20`) trên mỗi kết nối.
  - Giới hạn tần suất gọi Hub Join/Leave `30` lần/phút/kết nối (`MaxInvocationsPerMinute = 30`).
- Tuyệt đối không ghi `QueryString`, `access_token`, dữ liệu PHI hay payload nhạy cảm vào ASP.NET logs hoặc exception logs.

### 2.3. Phân quyền Hai Lớp (Two-Layer AuthZ) khi Tham gia Group

Client **không được truyền tên group tùy ý** và **không được tự truyền `servicePointId` cho luồng bệnh nhân**. Server tự chuẩn hóa khóa group sau khi kiểm tra quyền tại `ClinicalRealtimeAccessService`:

1. **`JoinVisitPaymentGroup(visitIdOrCode)` → `visit:{visitId}`**:
   - Đồng nhất quy tắc với `VisitChargesService`:
     - `PATIENT`: tái sử dụng `VisitPaymentAccess.LoadOwnedPatientIdsAsync` (`PatientAccessScope.WherePhiMemberOf` theo ADR-0045). Chỉ cho phép bệnh nhân chính chủ hoặc người thân `VERIFIED` qua `STAFF`/`OTP` còn hiệu lực; **từ chối liên kết `LEGACY`**, liên kết đã thu hồi và bệnh nhân khác.
     - Nhân sự (`RECEPTION`, `HOSPITAL_ADMIN`, `SYSTEM_ADMIN`): phải có tài khoản/hồ sơ nhân sự `ACTIVE`, có đúng permission nghiệp vụ (`PAYMENT_VIEW`, `PATIENT_CHECKIN`, `HOSPITAL_CONFIG`, hoặc `AUDIT_READ` cho `SYSTEM_ADMIN`) và đúng `HospitalId` của lượt khám (`SYSTEM_ADMIN` miễn ràng buộc một cơ sở).
2. **`JoinAppointmentQueueGroup(appointmentIdOrCode)` → `appointment:{appointmentId}` + `patient-bucket:sp:{servicePointId}` (hoặc `patient-bucket:room:{roomId}`)**:
   - Tái sử dụng `AppointmentReadModel.LoadOwnedAppointmentViewAsync` (ADR-0020).
   - Từ chối tham gia nếu lịch hẹn, lượt khám hoặc phiếu hàng đợi đã ở trạng thái `CANCELLED` hoặc `NO_SHOW`. Nếu lượt khám đã `COMPLETED`, không đăng ký vào `patient-bucket` dùng chung.
   - Server tự suy ra `servicePointId` / `roomId` hiệu lực từ `Visit` và `QueueEntry` (ưu tiên phòng đã gán/chuyển thực tế).
3. **`JoinServicePointGroup(servicePointId)` → `hospital:{hospitalId}:sp:{id}`** và **`JoinHospitalQueueGroup()` → `hospital:{hospitalId}:queue`**:
   - Chỉ cho phép nhân sự `ACTIVE` thuộc các vai trò lâm sàng/điều phối (`RECEPTION`, `DOCTOR`, `DIAGNOSTIC_STAFF`, `HOSPITAL_ADMIN`, `SYSTEM_ADMIN`) có permission hàng đợi/lâm sàng phù hợp và thuộc đúng `HospitalId` đang hoạt động.

### 2.4. Thu hồi Quyền Chủ động (Active Membership Revocation), Chuyển Phòng & Lease TTL

Do SignalR Group không tự thu hồi quyền sau khi kết nối đã tham gia, `ClinicalRealtimeConnectionRegistry` kết hợp `IRealtimeAccessRevoker` duy trì bảng ánh xạ membership trong bộ nhớ với hai lớp bảo vệ:

1. **Thu hồi & Di chuyển Tức thì từ Nghiệp vụ**:
   - Gỡ liên kết gia đình (`FamilyProfileService.RemoveFamilyMemberAsync`): xóa ngay mọi group `visit:*`, `appointment:*`, `patient-bucket:*` của hồ sơ người thân khỏi các kết nối đang mở của tài khoản chủ và gửi tín hiệu `MembershipRevoked`.
   - Khóa tài khoản khi đăng nhập sai quá ngưỡng, phát hiện reuse refresh token, đăng xuất (`AuthService`), vô hiệu hóa nhân sự (`StaffAccountAdminService`) hoặc đổi vai trò/bệnh viện nhân sự (`StaffAccountUpdateService`): thu hồi toàn bộ group của `userId` tương ứng.
   - Chuyển phòng / gán lại phòng khám (`VisitRoomTransferService`, `VisitRoomAssignmentService`): tự động gỡ kết nối của bệnh nhân khỏi `patient-bucket` phòng cũ, chuyển sang `patient-bucket` phòng mới và phát tín hiệu `QueueScopeChanged`.
   - Hủy lịch hẹn, hủy lượt hàng đợi hoặc cronjob `#824` tự động chuyển `NO_SHOW` (`AppointmentLifecycleProcessor`): phát tín hiệu cập nhật cuối cùng rồi gỡ kết nối khỏi `patient-bucket` để bệnh nhân không tiếp tục nhận tín hiệu phòng khám.
2. **Thời hạn Membership (`MembershipTtlMinutes = 5`)**:
   - Mỗi lần tham gia group chỉ có hiệu lực tối đa `5 phút`. Trước khi phát sự kiện và trong chu kỳ dọn dẹp, các membership hết hạn chưa được xác thực lại sẽ bị gỡ tự động khỏi SignalR Group; client đang mở màn hình thực hiện gia hạn định kỳ (`4 phút`) qua Hub để kiểm tra lại quyền từ cơ sở dữ liệu.

### 2.5. Hợp đồng Bản tin Tối thiểu Không chứa PHI (Zero-PHI Minimal Wire Contract)

| Sự kiện | Group đích | Payload JSON tối thiểu | Ghi chú bảo mật |
|---|---|---|---|
| `QueueChanged` | `patient-bucket:sp:{id}`, `patient-bucket:room:{id}` | `{"eventType":"QueueChanged","servicePointId":12}` | Không chứa `patientId`, `visitId`, `appointmentId`, họ tên, số phiếu hay trạng thái của bệnh nhân khác |
| `QueueChanged` | `appointment:{id}`, `hospital:{id}:queue`, `hospital:{id}:sp:{id}` | `{"eventType":"QueueChanged","servicePointId":12}` | Không chứa PHI/PII; client tự refetch query theo khóa sẵn có |
| `VisitPaymentChanged` | `visit:{visitId}` | `{"eventType":"VisitPaymentChanged","visitId":1002}` | Không chứa số tiền, số tài khoản, mã giao dịch hay trạng thái `PAID` |
| `QueueScopeChanged` | Kết nối bệnh nhân đổi phòng | `{"eventType":"QueueScopeChanged","appointmentId":501}` | Báo client đồng bộ lại scope phòng mới |
| `MembershipRevoked` | Kết nối vừa bị thu hồi quyền | `{"eventType":"MembershipRevoked","resourceKey":"...","reasonCode":"..."}` | Ngừng lắng nghe group đã mất quyền |

### 2.6. Phát Sự kiện Sau Commit (Post-Commit Fire-and-Isolate) & Độ phủ Nghiệp vụ

- Mọi lệnh gọi `IRealtimeNotificationPublisher` đặt **sau khi `SaveChangesAsync` / `tx.CommitAsync` đã hoàn tất thành công**. Nếu transaction rollback hoặc vi phạm khóa duy nhất (như SePay webhook gửi trùng `23505`), không phát bất kỳ sự kiện nào.
- Không dùng `Task.Run` mồ côi với scoped services; việc phát tới `IHubContext` được `await` trực tiếp với `CancellationTokenSource` giới hạn thời gian ngắn (`PublishTimeoutSeconds = 3`, tách khỏi request token đã đóng của client) và bọc `try/catch` ghi log đã làm sạch để lỗi SignalR không bao giờ làm hỏng giao dịch nghiệp vụ đã commit.
- Giữ tham số constructor tùy chọn ở cuối (`IRealtimeNotificationPublisher? realtimePublisher = null` mặc định về `NullRealtimeNotificationPublisher.Instance`) để tương thích 100% với các UnitTests hiện hữu; đồng thời kiểm thử tích hợp xác minh DI Production phân giải đúng `SignalRRealtimeNotificationPublisher`.
- Các luồng phát `QueueChanged` bao gồm: `CallNext`, cập nhật trạng thái/ưu tiên/hủy hàng đợi, qua lượt/khôi phục lượt, Safe Backfill, quay lại bác sĩ sau CLS, Check-in lịch hẹn, tạo lượt Walk-in, gán/chuyển phòng khám, mở/hoàn tất phiên khám, tạo/tiếp nhận/nhập kết quả chỉ định CLS, bệnh nhân hủy lịch và cronjob `#824` tự động chuyển `NO_SHOW`.

### 2.7. Vòng đời Kết nối Frontend, Coalescing & Chiến lược Polling Dự phòng

- Duy trì **một `ClinicalHubConnection` dùng chung duy nhất cho mỗi phiên đăng nhập (`userId` + `sessionEpoch`) trên một tab**. Khi logout (tại chỗ hoặc đồng bộ đa tab qua `BroadcastChannel`/`storage`) hoặc đổi tài khoản, lập tức `stop()`, hủy timer thử lại và xóa toàn bộ đăng ký group.
- `accessTokenFactory` luôn lấy token mới nhất và phối hợp với cơ chế single-flight + Web Locks refresh hiện hữu (ADR-0048).
- Bổ sung vòng lặp thử lại khi kết nối lần đầu thất bại (vốn không thuộc phạm vi `withAutomaticReconnect`), tự động đăng ký lại group và refetch ngay khi kết nối thành công / kết nối lại.
- **Gộp sự kiện (Coalescing `250ms` + jitter `0–150ms` cho hàng đợi bệnh nhân)** và chỉ invalidate đúng `queryKey` cụ thể (cấm gọi `queryClient.invalidateQueries()` toàn cục) nhằm tránh bão REST request khi phòng khám đông người chờ và bảo toàn tuyệt đối dữ liệu biểu mẫu lâm sàng đang nhập dở (`#825`).
- **Giữ HTTP Polling đa tầng (không tắt hẳn khi đang kết nối)**:
  - Khi SignalR `Connected` và đã tham gia group: Queue polling `30s`, Payment (`PENDING`) polling `15s`.
  - Khi `Reconnecting` hoặc `Disconnected`: Patient Queue polling `5s`, Staff Queue polling `15s`, Payment (`PENDING`) polling `5s`.
  - Khi đã `PAID`/đóng giao dịch hoặc đã logout: tắt polling (`false`).

## 3. Giới hạn Triển khai Đa Container & Hạ tầng Proxy

- Ở quy mô phát hành hiện tại của MedFlow AI, SignalR chạy chế độ **in-memory trên một API container duy nhất** (tương đồng với quyết định in-memory rate limiter tại ADR-0057).
- **Giới hạn khi mở rộng ngang**: Nếu chạy nhiều API container song song mà chưa cấu hình Redis Backplane hoặc Azure SignalR Service kèm sticky sessions cho negotiate, sự kiện phát tại container A sẽ không chuyển sang kết nối WebSocket đang giữ ở container B (khi đó cơ chế HTTP polling định kỳ 15s–30s vẫn đảm bảo tính đúng đắn cuối cùng, nhưng không đảm bảo độ trễ thời gian thực).
- **Yêu cầu Reverse Proxy**: Trong môi trường Development (`vite.config.ts`) và Production (Nginx / Envoy / Cloudflare), đường dẫn `/hubs` bắt buộc bật WebSocket upgrade (`ws: true` / `Upgrade: websocket`, `Connection: upgrade`) và HTTPS/WSS.

## 4. Kết quả Đo Kiểm Thực nghiệm (Integration Benchmark)

Kết quả đo thực tế trên môi trường kiểm thử tích hợp (`ClinicalRealtimeHubIntegrationTests.RealtimeDeliveryLatencyBenchmark_MeasuresEmpiricalP50AndP95OverWebSockets`, kết nối WebSocket thật tới `TestServer` + PostgreSQL `medflow_test` local Docker):

| Phép đo | Cỡ mẫu | P50 | P95 | Max | Ghi chú |
|---|---|---|---|---|---|
| **SignalR In-Memory Broker Hop** (`PublishQueueChangedAsync` → client WebSocket nhận frame JSON) | `N = 40` (sau `5` vòng warmup) | **`0.10 ms`** | **`0.12 ms`** | `0.15 ms` | Độ trễ thuần của kênh SignalR Hub + WebSocket frame |
| **PostgreSQL Commit + SignalR WebSocket End-to-End** (`Db.SaveChangesAsync()` → `PublishQueueChangedAsync` → client WebSocket nhận frame) | `N = 20` | **`4.33 ms`** | **`17.21 ms`** | `202.59 ms` | Bao gồm vòng ghi commit PostgreSQL và đẩy frame WebSocket |
| **Frontend Burst Coalescing** (`scheduleCoalescedInvalidation`, cửa sổ `250 ms`) | `25` tín hiệu `QueueChanged` liên tiếp | — | — | **`1` lần refetch** | Giảm từ `25` xuống đúng `1` lệnh `invalidateQueries` theo khóa hẹp, bảo toàn 100% cache biểu mẫu khác |

> **Lưu ý vận hành thực tế**: Số liệu `P95 = 17.21 ms` nêu trên phản ánh độ trễ nội bộ từ thời điểm PostgreSQL commit đến khi frame WebSocket tới client trên môi trường local/LAN; trên mạng Internet di động thực tế của bệnh nhân cần cộng thêm `1 RTT` mạng cho frame WebSocket và `1 RTT` cho lệnh `GET` REST refetch, do đó ngưỡng `< 300 ms` được xem là mục tiêu thiết kế cho môi trường mạng ổn định thay vì cam kết SLA tuyệt đối trên mọi điều kiện sóng di động.
