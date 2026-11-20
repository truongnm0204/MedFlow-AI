# 0045 - Family Link Verification & Identity Hash Hardening

- Ngay: 2026-11-14
- Trang thai: proposed (P0, cho commit truoc khi code)
- Lane: high-risk (PHI / authorization)
- Lien quan: `0040`, `0043`, `0044`

## 1. Hien trang (da xac minh)

- `FamilyProfileService.CreateFamilyMemberAsync` tao link `VERIFIED` khi trung `IdentificationHash` (198-241) va ca khi tao moi co hash (270).
- `ComputeIdentificationHash` dung SHA-256 khong khoa (862-867); `CreatePatientAtReceptionService` cung SHA-256 (63).
- `UpdateFamilyMemberAsync` (281-418) cho phep owner sua `FullName`, `DateOfBirth`, `Phone`, `Email` cua `member` bat ke member co tai khoan rieng hay khong.
- `WhereActiveMemberOf` chi yeu cau `verification_status = 'VERIFIED'`.
- Chuan hoa CCCD khong thong nhat: reception chi trim, family yeu cau 9/12 chu so.

## 2. Quyet dinh

### 2.1. Khong auto `VERIFIED`

- Link moi tao tu du lieu dinh danh vao trang thai `PENDING`, `verification_method = NULL` cho toi khi xac minh xong.
- Chi `VERIFIED` khi: staff co permission `PATIENT_IDENTITY_VERIFY` phe duyet, hoac chu ho so dong y qua OTP tren kenh cua ho.
- Khi xac minh: ghi `verification_method` (`STAFF` / `OTP`) va `verified_by_staff_id` (neu STAFF).

### 2.2. Quyen PHI cua link `LEGACY`

- Backfill link `VERIFIED` hien co thanh `verification_method = 'LEGACY'`; giu ban ghi de kiem toan.
- **Khong mac dinh coi `LEGACY` la da xac minh.** Tach pham vi:
  - `WhereActiveMemberOf` (dat lich, xem lich hen): `LEGACY` van duoc trong thoi gian gia han.
  - `WherePhiMemberOf` (vien phi, benh an, ket qua CLS): chi `verification_method IN ('STAFF','OTP')`.
  - Cac service doc PHI phai chuyen sang `WherePhiMemberOf`.
- Thoi han tai xac minh: **30 ngay** ke tu migration, co thong bao va ho tro tai quay. Het han ma chua xac minh:
  - Link giu ban ghi, `verification_status = 'PENDING'`, khong con quyen PHI.
  - Khong xoa lich su link.
- 30 ngay la de xuat van hanh, khong phai han phap ly.

### 2.3. Khong cho sua danh tinh cua ho so nguoi khac

- `UpdateFamilyMemberAsync` chi duoc sua `Relationship` va truong dong y/uy quyen.
- Cac truong `FullName`, `DateOfBirth`, `Phone`, `Email`, `IdentificationHash`:
  - Member chua co tai khoan (`UserId == null`) va link `STAFF`/`OTP`: co the sua, ghi audit.
  - Member da co tai khoan (`UserId != null`) hoac link khong xac minh: `403`, chuyen ho so benh an/chinh chu sua.
- Kiem tra quyen xem, quyen quan ly link, quyen sua ho so la ba kiem tra rieng.

### 2.4. Hash co khoa (expand → cutover → retire)

- Them `identification_hash_hmac` varchar(128) + `hash_version` smallint.
- Cong thuc: `HMAC-SHA256(key, lowercase_hex(SHA256(canonical_id)))`, key `IDENTITY_HASH_KEY` tu secret manager.
- Chuan hoa `canonical_id`: trim, bo khoang trang/gach ngang, chi chap nhan 9 hoac 12 chu so; ap dung o **moi** diem nhap (reception, family, sau nay).
- Backfill tu digest cu: `HMAC(key, identification_hash)` — khong can plaintext.
- Giai doan:
  1. Expand: them cot/index, dual-write, backfill, dry-run doi chieu so dong.
  2. Cutover: `Identity:HashMode = dual|hmac`; lookup uu tien HMAC, fallback SHA trong cua so ngan.
  3. Retire: tat fallback, migration rieng drop `identification_hash` sau khi xac nhan 0 truy van fallback.
- Key rotation: ADR rieng; `hash_version` cho phep ton tai nhieu version trong giai doan chuyen.
- Thieu key → fail closed, khong ghi hash moi.

## 3. Anh huong code

- `FamilyProfileService`: bo auto verify; lookup HMAC; chan sua danh tinh; gan method khi verify.
- `CreatePatientAtReceptionService`: dung helper hash chung + validate/format CCCD.
- `PatientAccessScope`: them `WherePhiMemberOf`.
- Cac service PHI (charges, diagnostic, medical history) doi sang scope PHI.
- `PatientFamilyLink` + configuration + migration.

## 4. Van hanh

- Thong bao den nguoi dung co link `LEGACY` truoc khi het 30 ngay.
- Bao cao link `LEGACY`, so luot tai xac minh, so lien ket bi ha quyen.
- Khong thu hoi hang loat ngay lap tuc; chi chan PHI theo muc 2.2.

## 5. Kiem thu bat buoc

| Case | Ky vong |
|---|---|
| Nhap CCCD nguoi khac | `PENDING`, khong doc PHI |
| Link `LEGACY` doc vien phi | `404`/`403` theo scope PHI |
| Link `LEGACY` dat lich | van duoc trong thoi gian gia han |
| Het 30 ngay | mat quyen PHI, ban ghi con |
| Owner sua ten/SDT cua member co tai khoan | `403` |
| Staff phe duyet | `VERIFIED` + method `STAFF` |
| Chu ho so OTP | `VERIFIED` + method `OTP` |
| Thieu `IDENTITY_HASH_KEY` | fail closed |
| CCCD sai dinh dang | `400` o moi diem nhap |
