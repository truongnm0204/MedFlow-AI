# 0041 — Generate QR Payment Contract (`UC18` / Issue `#162` / `M3-054`)

- Ngay: 2026-11-14
- Trang thai: accepted
- Xac nhan 2026-11-14: actor PATIENT, EMV tai cho tu hospital_policies, adopt dong PENDING walk-in khi amount khop.
- Lane: high-risk
- Phu thuoc: Decision `0040` (view charges). `#164` so huu chuyen trang thai `PAID` / `FAILED` / `EXPIRED` / `CANCELLED`.

Ba diem duoi day la de xuat de code. Can xac nhan truoc khi implement: actor chi `PATIENT`, payload EMV tai cho, va adopt dong `PENDING` cua walk-in.

## Context

`#160` da tra `amountDue`, `settlementStatus`, `canPayByQr`. Co do chi cho UI. Server tao QR phai tinh lai.

Schema `payments` co `payment_code` (40), `visit_id`, `amount` numeric(16,2), `external_reference` (150), `status` (16), `paid_at`, `created_at`. Khong co cot payload, expiry, idempotency. `external_reference` danh cho ma giao dich ngan hang o `#164`.

Walk-in (`CreateWalkInVisitService.TryAddWalkInPayment`) insert `payments` `status = PENDING`, `amount = servicePrice`, `payment_code` null. `paymentMethod` nam trong audit `WALK_IN_VISIT_CREATED`. Mac dinh walk-in la `CASHIER` khi request bo trong. Check-in hen khong insert payment.

`hospitals` khong co so tai khoan. `hospital_policies` da map, co `policy_value` jsonb va cua so hieu luc. `system_integrations` chua co entity.

Khong co unique index tren `payments`. Hai POST song song co the tao hai dong `PENDING`.

## Decision

1. **HTTP.** `VisitQrPaymentsController`. Body rong. Khong nhan amount, BIN, so tai khoan, payload.
   - `POST /api/v1/visits/{idOrCode}/payments/qr`
   - `POST /api/v1/patients/visits/{idOrCode}/payments/qr`
   - Khong alias reception.
   - `201` khi insert dong moi. `200` khi gan ma vao dong walk-in hoac tra lai dong da co ma. Ca hai tra `GenerateQrPaymentResponseDto`.
   - Khong `Location`. `#164` moi co GET ket qua.

2. **Layer 1:** `[Authorize(Roles = "PATIENT")]`. Role khac bi `403` o tang nay.

3. **Layer 2**, truoc khi tra bat ky field tien hoac tai khoan nao. Cung actor rule voi nhanh patient cua `#160`:
   - `actorUserId > 0`, user ton tai, `ACTIVE`, khong bi khoa. Sai → `401`, cung message voi `VisitChargesService`.
   - Token co `PATIENT` la du. Khong loai user chi vi ho con role khac.
   - `visit.patient_id` nam trong `AppointmentBookingRules.LoadOwnedPatientsAsync` (ban than + family link con hieu luc). Ngoai scope, hoac visit khong ton tai → `404` `Không tìm thấy lượt khám.`
   - Dinh danh visit: cung `ParseVisitIdentifier` cua `#160` (so duong hoac `visit_code` ≤ 40, trim, khong phan biet hoa, `#` dau bo). Sai → `400` `Mã lượt khám không hợp lệ.`
   - Hospital cua visit: cung tap nguon va cung ket luan voi Decision `0040` muc 5. Rong → `404`. Tu 2 id → `409` `Không xác định được cơ sở của lượt khám.`

4. **Tien.** Goi chung mot assesor voi GET `#160`. POST khong doc `canPayByQr` tu client va khong goi DTO GET lam nguon tien.
   - `amountDue` null, `hasUnpricedPayableCharges`, dong `INVALID`, hoac `settlementStatus` khac `UNPAID` → `409` `Chưa xác định được số tiền cần thanh toán.`
   - `amountDue = 0` → `409` `Lượt khám không có khoản phải thu.`
   - `visit.status` la `CANCELLED` hoac `NO_SHOW` → `409` `Lượt khám không thể thanh toán.`
   - `paymentMethod = CASHIER` → `409` `Lượt khám được thu tại quầy.` `paymentMethod` null (hen, khong co audit walk-in) duoc tao QR.
   - `amountDue` phai la so nguyen VND, `1..9999999999999`. Phan le hoac vuot dai EMV → `409` `Số tiền không phải số nguyên VND.`
   - Khong ghi `PAID`. Khong tru dan. Khong tao QR cho mot phan `amountDue`.

5. **Mot dong PENDING moi visit.** Migration `database/migrations/012_payments_one_pending_per_visit.sql` (khong sua `001`–`011`). Index:

   `ux_payments_one_pending_per_visit` UNIQUE ON `payments (visit_id)` WHERE `visit_id IS NOT NULL AND upper(btrim(status)) = 'PENDING'`.

   Neu da co hai dong `PENDING` cung visit, migration `RAISE EXCEPTION` va dung. Khong tu xoa tien. Them cung index vao `database/medflow_schema_v4_1.sql` de schema trong giong `011`.

   Trong transaction (`BeginTransactionAsync`):
   - 0 dong `PENDING`: insert `status = PENDING`, `amount = amountDue`, `paid_at` null, `external_reference` null, `created_at` UTC. `SaveChanges` lay `id`. `payment_code = "MF" + id` (toi da 21 ky tu).
   - 1 dong, `amount` bang `amountDue`, `payment_code` rong: gan `MF{id}`. Day la adopt walk-in. Khong doi amount.
   - 1 dong, `amount` bang `amountDue`, da co `payment_code`: tra lai dong do. Khong insert, khong audit them.
   - 1 dong, amount lech → `409` `Số tiền yêu cầu thanh toán không còn khớp khoản phải thu.` Khong sua amount, khong insert dong thu hai.
   - Tu 2 dong → `409` `Lượt khám đang có nhiều yêu cầu thanh toán mở.`
   - `DbUpdateException` unique `23505`: doc lai. Mot dong khop amount → tra dong do. Nguoc lai → `409`.

   `FAILED` / `CANCELLED` / `EXPIRED` khong chan lan tao moi khi settlement van `UNPAID` va khong con `PENDING`. `#164` dinh nghia cac status do.

6. **Tai khoan nhan.** `hospital_policies`, `policy_type = PAYMENT`, `policy_key = vietqr_account`, `queue_service_point_id` null, con hieu luc theo `QueuePolicyReader` (`effective_from` / `effective_to`). Nhieu dong: `policy_version` cao nhat, roi `id` cao nhat.
   - `policy_value`: `{ "bin": "970422", "accountNumber": "0123456789", "accountName": "BENH VIEN DEMO" }`.
   - `bin` dung 6 chu so. `accountNumber` 6–19 chu so. `accountName` 1–50 ky tu sau trim.
   - Khong co dong → `409` `Cơ sở chưa cấu hình VietQR.`
   - Json hoac field sai → `409` `Cấu hình VietQR của cơ sở không hợp lệ.`
   - Khong seed so tai khoan that. Test tu insert policy. Khong goi HTTP ngan hang. Khong them package QR.

7. **Payload.** `VietQrPayloadBuilder` dung EMVCo merchant-presented, NAPAS VietQR:
   - `01` = `12` (co amount).
   - Merchant account `38`, GUID `A000000727`, BIN, so tai khoan, service `QRIBFTTA`.
   - Currency `704`, amount la chuoi so nguyen khong dau cham, khong so 0 o dau.
   - Country `VN`. Merchant name tag `59`: `hospital.name` bo dau, in hoa, cat 25 ky tu ASCII. Rong sau khi loc → `MEDFLOW`.
   - Add info tag `62` / `08` = `payment_code`.
   - CRC16-CCITT-FALSE (poly `0x1021`, init `0xFFFF`) o tag `63`.
   - Payload tinh moi lan tra response tu policy dang hieu luc. Khong luu payload. Doi policy doi QR cua cung `payment_code`; ma chuyen khoan giu nguyen.

8. **DTO.** `GenerateQrPaymentResponseDto`: `VisitId`, `VisitCode`, `PaymentId`, `PaymentCode`, `Amount`, `Currency` (`VND`), `Status` (`PENDING`), `QrPayload`, `BankBin`, `AccountNumber`, `AccountName`, `TransferContent` (bang `PaymentCode`), `IssueKind` (`CREATED` | `ADOPTED` | `REUSED`).

9. **Audit** chi khi `CREATED` hoac `ADOPTED`. Ghi thang `audit_logs` vi `IAuditLogger.Add` khong co `hospital_id`.
   - `action = QR_PAYMENT_REQUESTED`, `entity_type = PAYMENT`, `entity_id` = id payment, `hospital_id` = hospital cua visit, `occurred_at` UTC, IP theo `ReceptionWalkInVisitController.GetIp`.
   - `change_data`: `visitId`, `paymentId`, `paymentCode`, `amount`, `currency`, `hospitalId`, `issueKind`.
   - Khong ghi BIN, so tai khoan, ten tai khoan, payload.

10. **Nam ngoai.** `#161` ve UI. `#164` doi status, webhook, het han, huy. Khong sua walk-in de ngung insert `PENDING`. Khong PNG. Khong thu mot phan. Khong rate-limit moi.

## Consequences

- Walk-in `VIETQR` co the thanh toan: POST nhan dong `PENDING` san co neu amount khop `amountDue`.
- Walk-in `CASHIER` bi `409` cho den khi co ticket doi phuong thuc.
- `#159` render `qrPayload`. Huong dan thu cong dung `accountName`, `accountNumber`, `transferContent`, `amount`.
- Unique index chan hai lan thu mo. Race tra ve dong thang cuoc khi amount khop.
