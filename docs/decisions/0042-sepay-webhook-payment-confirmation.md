# 0042 - SePay Webhook Payment Confirmation (`UC18` / Issue `#164` / `M3-056`)

- Ngay: 2026-11-14
- Trang thai: accepted
- Lane: high-risk
- Phu thuoc: Decision `0041` (phat hanh QR). `#162` chi ghi `PENDING`.
- Xac nhan 2026-11-14: giu QR EMV tai cho, them webhook SePay de tu xac nhan, gop vao `#164`.

## 1. Boi canh

`#162` phat QR va ghi mot dong `payments` `PENDING`. Tien vao tai khoan benh vien khong tu doi trang thai. `#164` can: hien thi ket qua, tu chuyen `PAID` khi co tien, va giai phong `PENDING` cu.

QR van la EMV NAPAS dung tai cho (`VietQrPayloadBuilder`). Khong doi payload, khong goi HTTP khi tao QR.

## 2. Kenh nhan

`POST /api/v1/payments/sepay/webhook`, `[AllowAnonymous]`.

- Header `Authorization: Apikey <key>`. Key doc tu `SePay:WebhookApiKey` (user-secrets/env).
- Key cau hinh rong → `401`. So sanh constant-time (`CryptographicOperations.FixedTimeEquals`).
- Tra `200 {"success":true}` trong 5s cho moi payload hop le ve cu phap. SePay retry 7 lan; endpoint idempotent.

## 3. Payload dung

`id`, `transferType`, `transferAmount`, `content`, `code`, `transactionDate`, `referenceCode`.

- Chi xu ly `transferType = in`. `out` tra `200`, khong ghi event.
- `id <= 0` hoac `transferAmount` khong duong → `400`.
- Ma thanh toan: nhan `code` khi khop `^MF\d{1,18}$`; nguoc lai regex `\bMF(\d{1,18})\b` tren `content` (khong phan biet hoa/thuong).
- Khong tin `accountNumber` de doi trang thai: payload khong luu tai khoan luc phat QR, so sanh voi policy hien tai se tu choi giao dich hop le sau khi doi policy.

## 4. Chong trung

Bang `payment_gateway_events`, unique `(provider, external_id)`.

- `ExistsAsync` chan som; unique index la chan cuoi. Vi pham `23505` → tra `200` `IGNORED`, xoa change tracker de khong flush nham.
- Moi giao dich chi duoc cong mot lan vao tong da nhan.

## 5. Doi soat

- Khong co `payment_code` khop → event `UNMATCHED`, khong doi `payments`.
- `payments.amount` null hoac `<= 0` → `UNMATCHED`.
- Tong = cac event `PAID/PARTIAL/OVERPAID` cua cung `payment_code` + lan nay.
  - `< amount` → `PARTIAL`, giu `PENDING`.
  - `= amount` → `PAID`.
  - `> amount` → `PAID` + `OVERPAID`. Chenh lech nam o event de doi soat tay (khong tao refund trong scope nay).
- Khi chuyen `PAID`: `paid_at` = `transactionDate` (gio VN, `Unspecified`); `external_reference` = `referenceCode` (cat 150).
- Audit `PAYMENT_CONFIRMED` (entity `PAYMENT`, `hospital_id` resolve tu visit): `paymentCode`, `provider`, `externalId`, `receivedAmount`, `totalReceived`, `expectedAmount`, `currency`, `outcome`. Khong ghi `content` tho, khong ghi ten benh nhan.

## 6. Trang thai

| Tu | Dieu kien | Ket qua |
|---|---|---|
| `PENDING`, `EXPIRED` | du tien | `PAID` |
| `PAID` | bat ky | giu nguyen, event `ALREADY_PAID` |
| `CANCELLED`, `FAILED` | bat ky | giu nguyen, event `ALREADY_CLOSED` |

`FAILED` va `CANCELLED` chua co nguon phat sinh: khong co su kien loi tu cong, va huy giao dich chua duoc yeu cau. Khong tu tao hai trang thai nay.

## 7. Het han `PENDING`

- Nguong 15 phut (`GenerateQrPaymentService.PendingExpiryMinutes`).
- Chi chay trong `POST /payments/qr` truoc khi kiem tra dong mo: `PENDING`, `created_at` khac null, cu hon nguong → `EXPIRED`.
- Khong chay trong `GET /charges` (`#160` read-only).
- Walk-in `PENDING` co `created_at` null → khong het han.
- Sau khi het han, `POST` tao dong moi. Unique index `012` chi chan dong `PENDING`.
- Webhook van nhan thanh toan cho dong da `EXPIRED` (tien da vao), theo muc 6.

## 8. Thoi gian

- `payments.created_at`, `payments.paid_at`: gio VN, `timestamp without time zone`, `DateTimeKind.Unspecified` (`AppointmentBookingRules.GetVietnamNow`).
- `payment_gateway_events.received_at`, `audit_logs.occurred_at`: UTC, `timestamptz`, `DateTimeKind.Utc`.

## 9. Ngoai scope

VA / tai khoan phu moi luot kham, hoan tien, webhook `out`, huy boi benh nhan, rate limit rieng, retry noi bo, doi soat tu dong lech tien.

## Consequences

- Bat buoc cau hinh `SePay:WebhookApiKey` truoc khi bat endpoint; thieu key thi moi request `401`.
- Nhieu instance API an toan nho unique index; hai giao dich khac nhau cung ma co the cung thay `PARTIAL` trong race hiem, lan webhook sau se chot `PAID`.
- `GET /charges` la mat doc trang thai cho `#161`; khong can endpoint tracking rieng.
