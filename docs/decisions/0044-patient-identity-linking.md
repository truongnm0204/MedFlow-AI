# 0044 - Patient Identity Linking (`Claim Existing Patient Profile`)

- Ngay: 2026-11-14
- Trang thai: proposed (cho review/commit truoc khi code)
- Lane: high-risk (auth + PHI + schema)
- Lien quan: `0040`, `0041`, `0042`, `0043`, `0045`
- Pham vi: Auth & Patient Management, khong thuoc UC18

## 1. Van de

`AuthService.RegisterAsync` luon tao `Patient` moi (`AuthService.cs` 59-68). Nguoi da co ho so tai benh vien (le tan tao, `UserId = null`) co hai `Patient` sau khi dang ky; ho so cu chua Visit/benh an/thanh toan. `PatientAccessScope` va `VisitPaymentAccess` chi doc theo `patients.user_id`.

Khong noi long ownership, khong doan theo ten/SDT. Cach dung: xac minh con nguoi roi gan `User` vao `Patient` goc; khong doi `visits.patient_id`.

## 2. Hop dong

### 2.1. Bang `patient_identity_claims`

| Cot | Kieu | Ghi chu |
|---|---|---|
| `id` | bigint identity | |
| `requester_user_id` | bigint FK `users.id` | tu token |
| `current_patient_id` | bigint NULL FK `patients.id` | ho so dang gan user luc gui |
| `target_patient_id` | bigint NULL FK `patients.id` | null khi ma khong ton tai |
| `requested_patient_code` | varchar(40) | snapshot |
| `hospital_id` | bigint FK `hospitals.id` | noi se xac minh truc tiep |
| `status` | varchar(24) | `PENDING`, `APPROVED`, `REJECTED`, `EXPIRED`, `CANCELLED`, `NEEDS_RECONCILIATION` |
| `created_at` / `expires_at` / `decided_at` | timestamptz | han 7 ngay |
| `decided_by_staff_id` | bigint NULL FK `staff.id` | |
| `decision_note` | varchar(500) | |

Trang thai "mo": `PENDING`, `NEEDS_RECONCILIATION`. Unique partial index:

```sql
CREATE UNIQUE INDEX uq_identity_claims_open_user
ON patient_identity_claims (requester_user_id)
WHERE status IN ('PENDING', 'NEEDS_RECONCILIATION');
```

Ly do: neu chi chan `PENDING`, nguoi dung co the gui claim moi ngay sau khi claim truoc chuyen sang `NEEDS_RECONCILIATION`.

Index them: `(target_patient_id, status)`, `(status, expires_at)`, `(hospital_id, status)`.

### 2.2. Endpoint

| Endpoint | Quyen | Ket qua |
|---|---|---|
| `POST /patients/me/identity-claims` | PATIENT + OTP step-up | `201` `{ claimId, status, message }` |
| `GET /patients/me/identity-claims[/{id}]` | PATIENT (cua minh) | khong tra du lieu ho so |
| `GET /reception/patient-identity-claims?status=` | permission `PATIENT_IDENTITY_VERIFY` | hang doi `PENDING` + `NEEDS_RECONCILIATION` |
| `POST /reception/patient-identity-claims/{id}/approve` | permission + dung `hospital_id` | `200` / `409` / `422` |
| `POST /reception/patient-identity-claims/{id}/reject` | nhu tren | `200`, bat buoc ly do |

Quyet dinh chot:

- **OTP step-up bat buoc** khi tao claim. Phien dang nhap + email da xac thuc khong du. OTP khong thay the xac minh danh tinh truc tiep.
- Claim gan `hospital_id` — benh nhan chon co so se den xac minh (danh sach hospital cong khai). Staff chi duyet duoc claim cua dung `hospital_id` cua minh **va** co permission `PATIENT_IDENTITY_VERIFY`.
- `VisitHospitalResolver` chi la thong tin doi chieu them, khong phai dieu kien duy nhat de cap quyen phe duyet danh tinh.
- `approvedBy` luon lay tu token.
- Claim khong con `PENDING` → `409`, ke ca da het han.

### 2.3. Transaction approve

1. Mo transaction; khoa claim (`SELECT ... FOR UPDATE`).
2. Doc `current_patient_id` theo `requester_user_id`; khoa dong patient hien tai va patient dich.
3. `target_patient_id` phai ton tai. Cap nhat co dieu kien:

```sql
UPDATE patients
SET user_id = @requesterUserId
WHERE id = @targetPatientId
  AND user_id IS NULL;
```

Bat buoc kiem tra `rows affected == 1`; neu 0 → `409` (da bi lien ket boi giao dich khac).

4. Kiem tra du lieu cua `current_patient` (truc tiep va gian tiep): `visits`, `appointments`, `patient_family_links` (ca hai chieu), `payments` qua visit, `medical_records`, `diagnostic_orders/results`.
5. Nhanh A - rong: `current.UserId = null`, `current.Status = 'ARCHIVED'` (khong xoa cung). Chi archive khi da chung minh rong o buoc 4.
6. Nhanh B - co du lieu: khong ghi de; claim → `NEEDS_RECONCILIATION`; mo ticket doi soat.
7. Cac claim `PENDING` khac cung `target_patient_id` → `REJECTED` voi note "ho so da duoc lien ket", cung transaction.
8. Audit `PATIENT_IDENTITY_LINKED` hoac `PATIENT_IDENTITY_RECONCILIATION_REQUIRED`.
9. Commit. Unique `patients.user_id` la chan cuoi.

### 2.4. Chong do tim ho so

- `POST` tra cung ma/thong diep cho ma dung va ma sai; khong tra ten/ngay sinh/SDT/lich su.
- Ma sai: van tao claim `PENDING`, `target_patient_id = null`; staff thay "khong tim thay" khi duyet.
- 1 claim mo/user; rate limit theo user + IP.

### 2.5. Rang buoc DB

- Unique `patients.user_id` partial (`where user_id is not null`) phai co truoc khi bat endpoint.
- Pre-check trung; migration fail neu con trung, khong tu xoa.
- EF `HasIndex(p => p.UserId).IsUnique()` phai mirror partial index; khong dung unique constraint toan bang.

### 2.6. Xu ly ho so trung user trong giai doan chuyen tiep

Moi cho doc "patient chinh cua user" phai qua mot helper chung:

- `PatientAccessScope.LoadActiveSelfAsync`
- `PatientProfileService` (dong 37-38)
- `VisitPaymentAccess.LoadOwnedPatientIdsAsync`
- `AuthService` (login response)

Helper: neu >1 patient cung `user_id` → `409` voi message cau hinh du lieu, khong `FirstOrDefault` im lang.

## 3. Khong doi

- Khong doi `visits.patient_id`, `payments`, `medical_records`, `diagnostic_*`.
- Khong noi long `PatientAccessScope`/`VisitPaymentAccess`.
- Khong tu dong lien ket khi dang ky hoac khi trung ten/SDT/CCCD.
- Khong dung `patient_family_links` de gia danh quyen so huu tai khoan.

## 4. Van hanh

- `NEEDS_RECONCILIATION` do bo phan quan ly ho so benh an / quan tri du lieu benh nhan xu ly; le tan chi tiep nhan, doi chieu ban dau, khong tu gop hai Patient.
- SLA de xuat (benh vien phe duyet): tiep nhan/phan loai 1 ngay lam viec; doi soat thuong 5 ngay; phuc tap/tranh chap chuyen cap trong 5 ngay, han rieng.
- Effort 32h la uoc luong ban dau; xac minh danh tinh, doi soat, migration HMAC va test phan quyen co the day len 45-60h.

## 5. He qua

- Sau lien ket, `GET /patients/me/visits` va `/visits/{id}/charges` hoat dong voi ho so goc.
- Can audit + backlog doi soat cho ho so co du lieu ca hai benh.
