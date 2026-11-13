# 0040 — View Charges & Payment Contract (`UC18` / Issue `#160` / `M3-052`)

- Ngay: 2026-11-14
- Sua sau review: 2026-11-14
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#160` can API doc khoan phi va trang thai thanh toan cua mot luot kham. Bang `visit_charges` va `payments` da co trong `database/medflow_schema_v4_1.sql`. EF chua map `visit_charges`. `Payment` chua map `payment_code`, `external_reference`, `created_at`.

Nguon du lieu hien tai:

- Walk-in (`ADR 0027`) insert `payments` (`status = PENDING`, `amount` = gia dich vu luc tiep nhan) va ghi `servicePrice`, `serviceId`, `paymentMethod` trong audit `WALK_IN_VISIT_CREATED`. Khong insert `visit_charges`.
- Check-in hen (`UC24`) khong insert charge hay payment.
- Chi dinh CLS khong insert `visit_charges`.

`visit_charges` khong co cot `quantity`. `payment_requirement` khong co CHECK. `#162` va `#164` chua co contract thanh toan tung phan hay nhieu lan thu QR.

`amountDue` la so tien duoc phep dem di thu. Thieu gia, requirement la, hoac payment `PAID` khong khop tong thi `amountDue` la `null`, khong phai `0` va khong phai tong da biet.

## Decision

1. **HTTP (`VisitChargesController`)**, `200` → `VisitChargesResponseDto`:
   - `GET /api/v1/visits/{idOrCode}/charges` (canonical)
   - `GET /api/v1/reception/visits/{idOrCode}/charges`
   - `GET /api/v1/patients/visits/{idOrCode}/charges`
   - Khong alias ghi. Khong body.

2. **Layer 1**: `[Authorize(Roles = "PATIENT,RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]`.
   `DOCTOR`, `PHARMACY_STAFF`, `DIAGNOSTIC_STAFF` bi chan o layer 1. Neu lot qua, service tra `403`.

3. **Layer 2** (`VisitChargesService`), truoc khi tra bat ky field tien nao:
   - `actorUserId > 0`, user ton tai, `ACTIVE`, khong bi khoa. Sai → `401`.
   - `PATIENT`: `visit.patient_id` nam trong scope `AppointmentBookingRules.LoadOwnedPatientsAsync` (ban than + family link con hieu luc). Khong thuoc scope, hoac visit khong ton tai → `404` cung message `Không tìm thấy lượt khám.`
   - `RECEPTION` / `HOSPITAL_ADMIN`: staff active, hospital active. Hospital cua visit (Decision 5) phai trung `staff.hospital_id`. Lech → `403`. Khong suy ra hospital tu hospital cua nguoi goi.
   - `SYSTEM_ADMIN`: khong so sanh hospital nguoi goi, nhung van ap Decision 5 (mau thuan → tu choi, khong resolve duoc → `404`).

4. **Dinh danh visit**: id so duong, hoac `visit_code` toi da 40 ky tu, trim, khong phan biet hoa. Rong, dai hon, hoac so `<= 0` → `400` `Mã lượt khám không hợp lệ.`

5. **Hospital cua visit — khong lay QueueEntry dau tien.**
   Thu thap moi `hospital_id` khac null. Bo qua nguon null, khong coi null la mot phieu bau.

   Nguon goc luot kham:
   - Co `appointment_id`: `appointment → examination_slots → services.hospital_id`, va `doctors.hospital_id` cua slot neu co.
   - Co audit `WALK_IN_VISIT_CREATED` (`entity_type = VISIT`, `entity_id` = id visit): `audit_logs.hospital_id`.

   Nguon doi chieu, lay het, khong dung o dong dau:
   - `hospital_rooms.hospital_id` qua `visit.examination_room_id`
   - `doctors.hospital_id` qua `visit.assigned_doctor_id`
   - `staff.hospital_id` qua `visit.checked_in_by_staff_id`
   - Moi `queue_entries` cua visit: `queue_service_points.hospital_id` cua `service_point_id` va `current_service_point_id` khi cac id do khac null

   Ket luan:
   - Tap rong → `404` `Không tìm thấy lượt khám.` Khong gan hospital cua actor.
   - Tu 2 id tro len → `409` `Không xác định được cơ sở của lượt khám.` Khong tra charges. Ap cho ca patient da qua ownership.
   - Dung 1 id → do la hospital cua visit.

   Patient bi `404` vi ngoai scope truoc khi xet hospital. Family link het han hoac thu hoi khong nam trong scope.

6. **Dong phi. `unit_price` la so tien cua dong, khong nhan quantity.**

   `payment_requirement` sau trim, khong phan biet hoa:
   - `REQUIRED` → phai thu
   - `null` → cung `REQUIRED`. Day la quy tac nghiep vu cua contract, khong phai gia tri tam.
   - `WAIVED` → khong thu. Van tra trong list. Khong cong vao `payableTotal`.
   - Gia tri khac, hoac `unit_price < 0` → khong phai mien phi. `payable = null`, `chargeDataStatus = INVALID`.

   Co it nhat mot `visit_charges` cua visit: chi tra cac dong do. `chargeSource = PERSISTED`. Khong them dong suy ra.

   Khong co dong nao: dung mot dong kham, `chargeSource = DERIVED`, `chargeId = null`.
   - Gia, theo thu tu, dung nguon dau tien co so `>= 0`:
     1. Khong ap dung o nhanh nay (chua co charge).
     2. Walk-in: `servicePrice` trong `change_data` cua audit `WALK_IN_VISIT_CREATED`.
     3. Lich hen: `service_prices` hieu luc tai `visit.checked_in_at` (neu null thi gio Viet Nam), cung cua so `effective_from` / `effective_to` nhu walk-in. Khong fallback `200_000`.
   - `payments.amount` khong bao gio la don gia. Chi de hien thi giao dich.
   - Khong co gia dang tin: `unitPrice = null`, `payable = true`, `chargeDataStatus = UNPRICED`. Khong ghi `0`.
   - Dich vu lay tu audit walk-in hoac tu slot. Khong co dinh danh dich vu van tra mot dong null, khong bi gia tu payment.
   - Khong sinh dong tu `diagnostic_orders`.

   `payableTotal` la tong da biet: cong `unitPrice` cua dong `payable = true` va `unitPrice >= 0`. Dong `WAIVED`, dong `INVALID`, dong chua co gia khong cong vao. `payableTotal = 0` khi chua co dong nao co gia hop le. Gia tri nay khong phai so tien duoc phep thu.

   `hasUnpricedPayableCharges = true` khi co dong `payable = true` ma `unitPrice` null, hoac co dong `payable = null`.

7. **Tien con phai tra va co QR. Khong suy dien thanh toan tung phan.**

   `latestPayment` = dong `payments` co `id` lon nhat. Chi de hien thi: `paymentId`, `paymentCode`, `paymentStatus` (status da luu; khong co dong → `UNPAID`), `paymentAmount`, `paidAt`, `externalReference`. `paymentMethod` lay tu audit walk-in (`VIETQR` hoac `CASHIER`), khong co thi `null`. Khong sua status.

   Payment duoc cong nhan da thu: `status` la `PAID`.
   - Trung giao dich: khoa la `payment_code` neu khac rong, khong thi `external_reference` neu khac rong, khong thi chinh `id`. Trung khoa: giu dong `id` lon nhat, khong cong don.
   - `recognizedPaidTotal` = tong amount sau khi gop khoa. Amount `PAID` bi null → khong doi soat duoc.

   `settlementStatus` va `amountDue` (bang phi da du gia, `hasUnpricedPayableCharges = false`):
   - Co `PAID` thieu amount → `UNDETERMINED`, `amountDue = null`.
   - Khong co dong `PAID` nao → `UNPAID`, `amountDue = payableTotal`. `0` o day nghia la da xac dinh khong phai thu (moi dong `WAIVED` hoac khong co dong phai thu), khong nghia la chua biet.
   - Dung mot giao dich `PAID` va amount cua no bang `payableTotal` → `PAID`, `amountDue = 0`.
   - Mot `PAID` khong bang tong, hoac tu hai giao dich `PAID` khac khoa → `MISMATCH`, `amountDue = null`. Khong tra `payableTotal - recognizedPaidTotal`. Khong tra lai ca `payableTotal`. Khong tra `0`. `recognizedPaidTotal` van tra de mot `FAILED` moi hon khong xoa lich su `PAID`.

   Bang phi chua du (`hasUnpricedPayableCharges = true`): `UNDETERMINED`, `amountDue = null`, ke ca khi `payableTotal` da cong duoc mot phan.

   `currency = VND`.

   `canPayByQr` chi la co cho UI. `#162` phai tu kiem tra quyen, trang thai visit, do day du cua bang phi va so tien. Khong tin co nay tu client.

   `canPayByQr = true` chi khi tat ca deu dung:
   - `visit.status` khong phai `CANCELLED` va khong phai `NO_SHOW`
   - `hasUnpricedPayableCharges = false` va khong co dong `INVALID`
   - `settlementStatus = UNPAID`
   - `amountDue` khac null va `> 0`
   - khong co dong `payments.status = PENDING` (lan thu dang mo thuoc `#162`, khong tao them QR)
   - `paymentMethod` khac `CASHIER`

   Visit `CANCELLED` hoac `NO_SHOW` van `200`. `canPayByQr = false`.

8. **DTO.** `0` khong dung cho ca "khong phai tra" va "chua biet". Truong chua biet la `null`.

   `VisitChargeLineDto`: `ChargeId` (`long?`), `ChargeSource` (`PERSISTED`|`DERIVED`), `ServiceId` (`long?`), `ServiceCode` (`string?`), `ServiceName` (`string?`), `DiagnosticOrderId` (`long?`), `UnitPrice` (`decimal?`), `PaymentRequirement` (`string?`, gia tri goc), `Payable` (`bool?`), `ChargeDataStatus` (`VALID`|`INVALID`|`UNPRICED`).

   `VisitChargesResponseDto`: `VisitId`, `VisitCode`, `VisitStatus`, `PatientId`, `Charges`, `PayableTotal` (`decimal`, tong da biet), `HasUnpricedPayableCharges`, `AmountDue` (`decimal?`), `Currency`, `RecognizedPaidTotal` (`decimal`), `SettlementStatus` (`UNPAID`|`PAID`|`MISMATCH`|`UNDETERMINED`), `PaymentId`, `PaymentCode`, `PaymentStatus`, `PaymentAmount` (`decimal?`), `PaymentMethod`, `PaidAt`, `ExternalReference`, `CanPayByQr`.

9. **Khong ghi.** GET khong `SaveChanges`, khong audit, khong insert charge hay payment.

10. **Khong migration SQL.** Chi map cot da co. `VisitCharge.UnitPrice` la `numeric(14,2)`. `Payment.Amount` la `numeric(16,2)`. Moc thoi gian la `timestamp without time zone`.

## Consequences

- `#159` khong duoc doi `amountDue = 0` thanh "da het no" khi truong do null.
- `#162` doc lai quy tac nay tren server. Co `canPayByQr` tu `#160` khong du de tao QR.
- Phi CLS chi xuat hien khi da co `visit_charges`. Check-in va walk-in khong bi sua o ticket nay.
- Walk-in da co payment `PENDING` se co `canPayByQr = false` cho den khi `#162` dinh nghia tai su dung lan thu do.
