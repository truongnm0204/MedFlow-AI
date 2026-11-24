# 0052 — Service Pricing Management & Pharmacy FEFO Batch Selection Contract

- Ngày: 2026-11-25
- Trạng thái: accepted
- Lane: high-risk
- Liên quan: `0038`, `0039`, `0046`, `0049`, `0050`
- Issues: `#818`, `#206`, `#208`, `#146`

## Bối cảnh

1. **Quản lý Biểu phí Dịch vụ (`UC52` / `#206`, `#208`)**:
   - Giao diện `/admin/pricing` (`service-pricing-page.tsx`) và modal cập nhật/lịch sử giá (`service-price-update-modal.tsx`, `service-price-history-modal.tsx`) hiện đang gọi tạm `GET/PUT /api/v1/admin/examination-services`, chỉ gửi trường `price` mà chưa lưu được quyết định pháp lý (`decisionNumber`), người phê duyệt (`appliedBy`), khoảng hiệu lực (`validFrom`..`validTo`), ghi chú điều chỉnh (`note`), cũng như chưa truy xuất được lịch sử thay đổi giá thực tế từ máy chủ và chưa ghi vết kiểm toán chuyên biệt `SERVICE_PRICE_UPDATED`.
   - Bảng `service_prices` trong schema v4.1 lưu `id`, `service_id`, `price_amount`, `effective_from`, `effective_to`; cần bổ sung tầng dịch vụ quản trị biểu phí độc lập mà không thay đổi schema DB đã commit.

2. **Chọn Lô thuốc Xuất kho theo chuẩn FEFO (`UC45` / `#146`)**:
   - Màn hình `/pharmacy/prescriptions/:prescriptionCode/fefo-batches` (`fefo-batch-selection-page.tsx`) đang tái sử dụng tạm endpoint `/prepare` và tự tính ngày hết hạn bằng đồng hồ trình duyệt (`Date.now()`), chưa có API chuyên biệt `/fefo-batches` để sắp xếp lô theo `ExpiryDate ASC` dựa trên `HospitalClock` (`Asia/Ho_Chi_Minh`) và chưa kiểm soát nghiệp vụ bắt buộc nhập lý do hợp lệ khi Dược sĩ chọn lô có hạn dùng xa hơn trong khi lô hạn gần hơn vẫn còn đủ tồn kho khả dụng.

## Quyết định

### 1. Hợp đồng API Quản lý Giá Dịch vụ (`AdminServicePricesController` & `ServicePriceAdminService`)

- **Endpoints**:
  - `GET /api/v1/admin/service-prices`: Danh sách biểu phí hiện hành, chỉ số tổng hợp (`ServicePricingMetricsDto`) và lịch sử giá của các dịch vụ thuộc phạm vi cơ sở y tế (`page` mặc định `1`, `pageSize` từ `1..200`, lọc theo `keyword`, `departmentId`, `category`/`serviceType`, `status`/`isActive`, `minPrice`, `maxPrice`, và `hospitalId` dành cho `SYSTEM_ADMIN`).
  - `GET /api/v1/admin/examination-services/{idOrCode}/prices` (alias: `GET /api/v1/admin/service-prices/{idOrCode}`, `GET /api/v1/admin/services/{idOrCode}/prices`): Chi tiết giá hiện hành và toàn bộ lịch sử quyết định giá của một dịch vụ theo thời gian giảm dần (`EffectiveFrom DESC, Id DESC`).
  - `POST|PUT /api/v1/admin/examination-services/{idOrCode}/prices` và `POST|PUT /api/v1/admin/service-prices`:
    - Xác thực `PriceAmount >= 0` (cho phép `0` VNĐ đồng bộ với `ADR-0049`) và `<= 1_000_000_000` VNĐ, tối đa 2 chữ số thập phân.
    - Xác thực `ValidFrom <= ValidTo` (nếu có truyền `ValidTo`); khi chỉ truyền ngày `yyyy-MM-dd` cho `ValidTo`, chuẩn hóa về cuối ngày `23:59:59` theo giờ bệnh viện để khoảng hiệu lực trong cùng một ngày vẫn hợp lệ.
    - Tự động đóng hiệu lực các bản ghi `service_prices` cũ đang mở hoặc chồng lấn phía sau (`EffectiveTo == null || EffectiveTo > newEffectiveFrom` -> gán `EffectiveTo = newEffectiveFrom`).
    - Tạo bản ghi `service_prices` mới và ghi `audit_logs` với `Action = "SERVICE_PRICE_UPDATED"`, `EntityType = "SERVICE"`, `EntityId = service.Id`, `HospitalId = service.HospitalId`, `Reason = note`, và `ChangeData` JSONB lưu `servicePriceId`, `decisionNumber`, `appliedBy`, `note`, `previousPriceAmount`, `priceAmount`, `validFrom`, `validTo`.
    - Khi đọc danh sách hoặc lịch sử giá, ghép metadata (`decisionNumber`, `appliedBy`, `note`) từ `audit_logs` (`SERVICE_PRICE_UPDATED`) theo `servicePriceId`; đối với dữ liệu seed lịch sử chưa có bản ghi kiểm toán thì trả `null` (Frontend hiển thị `"Chưa cập nhật"`, không suy diễn dữ liệu giả).

- **Phân quyền 2 lớp (Fail-Closed theo `ADR-0050`)**:
  - Layer 1: `[Authorize(Roles = "HOSPITAL_ADMIN,SYSTEM_ADMIN")]`.
  - Layer 2: Kiểm tra `User` `ACTIVE` và không bị khóa, vai trò DB hợp lệ (`HOSPITAL_ADMIN` hoặc `SYSTEM_ADMIN`), quyền DB `PermissionCodes.HospitalConfig` (`HOSPITAL_CONFIG`). Với `HOSPITAL_ADMIN`, bắt buộc hồ sơ `Staff` `ACTIVE` gắn `HospitalId` thuộc cơ sở đang hoạt động và chặn `403 Forbidden` mọi truy cập/cập nhật chéo cơ sở y tế.

### 2. Hợp đồng API Chọn Lô thuốc FEFO (`PharmacyFefoBatchesController` & `FefoBatchSelectionService`)

- **Endpoints**:
  - `GET /api/v1/pharmacy/prescriptions/{idOrCode}/fefo-batches` (alias: `GET /api/v1/pharmacy/fefo-batches/{idOrCode}`):
    - Tính `today = HospitalClock.GetToday(timeProvider)` (`Asia/Ho_Chi_Minh`).
    - Phân loại từng lô thuốc (`medicine_batches`):
      - `ExpiryDate < today` (hoặc thiếu `ExpiryDate`) -> `Status = "EXPIRED"`, `IsSelectable = false`.
      - `IsBlocked == true` hoặc `Medicine.IsActive == false` hoặc `QuantityOnHand < prescribedQuantity` -> `Status = "BLOCKED"`, `IsSelectable = false` kèm `BlockReason` rõ ràng.
      - Các lô hợp lệ còn lại sắp xếp tăng dần theo `ExpiryDate ASC, Id ASC`: lô hợp lệ có hạn dùng sớm nhất được đánh dấu `Status = "FEFO_RECOMMENDED"` (`IsFefoRecommended = true`), các lô hợp lệ tiếp theo mang `Status = "AVAILABLE"`.
    - Mặc định trả các lô khả dụng đứng đầu (`ExpiryDate ASC`) và giữ các lô `EXPIRED`/`BLOCKED` ở cuối danh sách với cờ khóa để giao diện Dược sĩ hiển thị cảnh báo trực quan (hỗ trợ `?includeUnavailable=false` khi chỉ cần lấy tập lô khả dụng).
  - `POST|PUT /api/v1/pharmacy/prescriptions/{idOrCode}/fefo-batches` (alias: `POST|PUT /api/v1/pharmacy/fefo-batches/{idOrCode}`):
    - Chặn đơn thuốc ở trạng thái `DISPENSED` hoặc `CANCELLED` (`409 Conflict`).
    - Chặn tuyệt đối lô hết hạn (`ExpiryDate < today`), lô bị khóa (`IsBlocked == true`), thuốc ngừng hoạt động (`Medicine.IsActive == false`), hoặc lô không đủ tồn kho khả dụng (`QuantityOnHand < allocatedQuantity`) với `409 Conflict`.
    - Quy tắc ngoại lệ FEFO: nếu Dược sĩ chọn lô có `ExpiryDate` xa hơn lô hợp lệ sớm nhất đang còn đủ tồn kho (`selectedBatch.ExpiryDate > earliestEligibleBatch.ExpiryDate`), bắt buộc phải cung cấp lý do hợp lệ (`overrideReason` từ 5 đến 500 ký tự); nếu thiếu trả `400 Bad Request` (`ValidationException`).
    - Lưu phân bổ lô dự kiến vào `medication_dispenses` / `dispense_items` (cập nhật trạng thái đơn sang `PREPARING` nếu đang ở `ISSUED`/`PENDING`, giữ nguyên `READY` nếu đã hoàn tất kiểm tra GPP) và ghi `audit_logs` `FEFO_BATCH_SELECTED`.

- **Phân quyền 2 lớp & Cách ly Cơ sở (`ADR-0050`)**:
  - Layer 1: `[Authorize(Roles = "PHARMACY_STAFF")]`.
  - Layer 2: Kiểm tra `User` `ACTIVE`/không khóa, vai trò DB `PHARMACY_STAFF`, quyền DB `PermissionCodes.MedicineDispense` (`MEDICINE_DISPENSE`), hồ sơ `Staff` và `Hospital` `ACTIVE`, đồng thời cách ly nghiêm ngặt đơn thuốc và danh mục thuốc theo `Staff.HospitalId`.

### 3. Đồng bộ Frontend

- `admin-services`: Đồng bộ `examination-service.api.ts`, `service-price.schema.ts` (`price >= 0`), `service-pricing-page.tsx`, `service-price-update-modal.tsx` và `service-price-history-modal.tsx` gọi trực tiếp `/admin/service-prices` và `/admin/examination-services/{idOrCode}/prices`.
- `pharmacy`: Đồng bộ `pharmacy.api.ts`, `use-pharmacy-workbench.ts`, `fefo-batch-selector-card.tsx` và `fefo-batch-selection-page.tsx` gọi trực tiếp `/pharmacy/prescriptions/{idOrCode}/fefo-batches`, hiển thị ô nhập lý do bắt buộc khi chọn lô hạn xa hơn lô đề xuất FEFO.

## Hệ quả

- **Tích cực**: Khép kín luồng quản trị biểu phí `UC52` (hỗ trợ giá `0` VNĐ, đóng hiệu lực giá cũ tự động, truy vết đầy đủ quyết định giá qua `SERVICE_PRICE_UPDATED`) và luồng chọn lô thuốc `UC45` chuẩn FEFO theo múi giờ bệnh viện mà không cần migration DB hay sửa các file dùng chung nhạy cảm.
- **Ràng buộc**: Giữ nguyên cấu trúc bảng `service_prices` và `dispense_items`; metadata quyết định giá và lý do vượt ưu tiên FEFO được bảo toàn nguyên vẹn trong `audit_logs.change_data` (JSONB).
