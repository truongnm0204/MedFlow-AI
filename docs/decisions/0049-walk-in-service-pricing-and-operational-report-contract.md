# 0049 — Walk-in Service Pricing Guard, Dispensing Receipt Non-Fabrication & Operational Report Contract

- Ngay: 2026-11-24
- Trang thai: accepted
- Lane: high-risk
- Lien quan: `0027`, `0040`, `0044`, `0046`, `0047`

## Context

Qua ra soat toan dien codebase (`TASK-0007`), ba diem lech nghiep vu nghiem trong can duoc chuan hoa:

1. **Walk-in Service Pricing (`UC25` / cap nhat `ADR-0027`)**:
   - `CreateWalkInVisitService` va `DoctorCatalogService` dang fallback ve gia cung `200_000m` VND khi dich vu chua co `ServicePrice` hieu luc, dong thoi dieu kien `PriceAmount > 0` tu choi nham dich vu duoc cau hinh hop le voi gia `0` VND.
   - MedFlow AI chi xu ly kham ngoai tru (khong xu ly cap cuu), nen moi dich vu kham vang lai bat buoc phai co bang gia hieu luc truoc khi tiep nhan.

2. **Confirm Medicine Dispensing Receipt (`UC46` / dong bo `ADR-0046` voi `ADR-0047`)**:
   - `ConfirmMedicineDispensingService` dang gan cung don gia thuoc `15_000m` VND, vi tri ke modulo (`Ke A-01, Ngan 2`) va tu ghi de `totalAmount` tren bien nhan khi chua co `Payment` `PAID`, mau thuan truc tiep voi `ADR-0047` (`DispensingResultService`).

3. **Admin Operational Report (`UC56 — View Operational Report`)**:
   - Man hinh `SCR-ADM-RPT-01` (`operational-reports-page.tsx`) dang chay tren du lieu gia co dinh (`22/10/2026`) vi chua co endpoint tong hop bao cao van hanh theo khoang thoi gian va khoa.

*(Luu y ve Ho so Benh nhan vs Tai khoan: `patients.user_id` cho phep `NULL` va quy trinh lien ket danh tinh `ADR-0044` da dap ung day du nguyen tac bat buoc ho so `Patient` nhung khong bat buoc tai khoan `User` khi tiep nhan Walk-in — giu nguyen khong thay doi.)*

## Decision

### 1. Walk-in Service Pricing (`409 Conflict` & `PriceAmount >= 0`)

- **Loai bo hoan toan `DefaultServicePrice = 200_000m` va `DefaultExaminationPrice = 200_000m`**.
- Khi dang ky luot kham Walk-in (`POST /api/v1/reception/visits/walk-in` va cac alias trong `ADR-0027`):
  - Kiem tra `Service.HospitalId == effectiveHospitalId` va tim `ServicePrices` thoa man:
    - `ServiceId == service.Id`
    - `PriceAmount != null && PriceAmount >= 0`
    - `(EffectiveFrom == null || EffectiveFrom <= nowLocal)` va `(EffectiveTo == null || EffectiveTo >= nowLocal)` (gio benh vien `UTC+7`).
  - **Truoc khi mo transaction** tao `Visit`, `QueueEntry`, `Payment`, `AuditLog`: neu khong tim thay bang gia hop le (chua cau hinh, da het hieu luc, hoac chua den ngay hieu luc) → tra `409 Conflict` (`ConflictException`):
    `"Dịch vụ khám chưa được cấu hình bảng giá hiệu lực. Vui lòng liên hệ quản trị viên bệnh viện."`
  - **Gia bang `0` duoc cau hinh hop le (`PriceAmount == 0m`)**: cho phep dang ky, luu snapshot `servicePrice = 0` vao audit `WALK_IN_VISIT_CREATED`, dat `paymentStatus = "WAIVED"` va khong tao dong `Payment` `PENDING` 0d (tuong thich `ADR-0040`).
  - **Gia duong (`PriceAmount > 0m`)**: tao dong `Payment` `PENDING` voi `Amount = servicePrice` va luu snapshot vao `WALK_IN_VISIT_CREATED` de thay doi bang gia sau nay khong anh huong luot kham cu.
- **Danh muc Walk-in (`GET /api/v1/reception/walk-in/catalog`) & `DoctorCatalogService`**:
  - Truong `Price` tra `decimal?` (`null` khi thieu gia hieu luc) kem `PriceFormatted = "Chưa cập nhật"` (va `HasEffectivePrice = false` tren catalog walk-in).
- **Du lieu lich su (`UNPRICED`)**:
  - `GetWalkInVisitByIdOrCodeAsync` va `VisitChargesService` (`ADR-0040`) giu kha nang doc luot kham lich su chua co gia (`ServicePrice = null`, `PriceFormatted = "Chưa cập nhật"`, `PaymentStatus = "UNPRICED"`), khong nem `409` khi chi doc lai luot kham cu.

### 2. Dong bo `ConfirmMedicineDispensingService` theo `ADR-0047`

- Loai bo don gia gia `15_000m`, phep nhan `computedTotal` va vi tri ke gia sinh theo modulo.
- Tra `UnitPrice = null`, `LineTotal = null`, `UnitPriceFormatted = "Chưa có dữ liệu"`, `LineTotalFormatted = "Chưa có dữ liệu"`, `ShelfLocation = "Chưa cập nhật"`, `TotalAmount = null`, `TotalAmountFormatted = "Chưa có dữ liệu"`, va giu `PaymentSummary` chi phan anh trang thai thanh toan cap luot kham.

### 3. Hop dong API `UC56` — `GET /api/v1/admin/reports/operational`

- **Endpoint**: `GET /api/v1/admin/reports/operational`
  - Query params: `fromDate` (`yyyy-MM-dd`, optional — mac dinh 7 ngay gan nhat tinh den hom nay theo gio `UTC+7`), `toDate` (`yyyy-MM-dd`, optional), `departmentId` (`long?`, optional), `serviceId` (`long?`, optional), `hospitalId` (`long?`, optional — chi danh cho `SYSTEM_ADMIN`).
  - Validation (`400`): `fromDate > toDate` hoac khoang thoi gian > 366 ngay.
- **Phan quyen 2 lop**:
  - Layer 1: `[Authorize(Roles = "HOSPITAL_ADMIN,SYSTEM_ADMIN")]`.
  - Layer 2: Kiem tra `User` active/unlocked, DB role hop le, permission (`REPORT_VIEW` / `HOSPITAL_CONFIG` khi `RolePermissions` da cau hinh). Voi `HOSPITAL_ADMIN`, bat buoc `Staff` active va co lap nghiem ngat theo `Staff.HospitalId` (`403 Forbidden` neu truyen `hospitalId` khac).
- **Chi so tra ve (khong dung du lieu gia)**:
  - Summary KPIs: `totalVisits`, `totalAppointments`, `completedVisits`, `walkInVisits`, `noShowAppointments`, `averageWaitMinutes` (`double?`), `medianWaitMinutes` (`double?`). Khi khong co `QueueEntry` nao du moc `ArrivedAt` va `CalledAt ?? StartedAt` (`>= ArrivedAt`), `averageWaitMinutes` va `medianWaitMinutes` tra `null` (khong tra `0`).
  - Breakdowns: `dailyVisits` (xu huong theo ngay), `hourlyIntake` (khung gio trong ky), `departmentVisits` (phan bo theo khoa), `visitStatusSegments` (ty trong trang thai), va `departmentRows` phuc vu bang phan tich + xuat CSV. Khi khong co du lieu trong ky, tra so dem `0` va danh sach rong `[]`.

## Consequences

- **Duoc**: Cham dut hoan toan tinh trang thu sai phi kham `200.000đ` / gia thuoc `15.000đ` tren bien nhan, ho tro dich vu 0đ hop le, va hoan thien `UC56` tren du lieu PostgreSQL that ma khong can thay doi schema DB.
- **Rang buoc**: Cac test va man hinh Frontend doc `Price` cua `WalkInCatalog` / `DoctorCatalog` can xu ly kieu nullable (`decimal?` / `number | null`) va hien thi `"Chưa cập nhật"` khi `Price == null`.
