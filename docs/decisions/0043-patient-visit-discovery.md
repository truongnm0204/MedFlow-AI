# 0043 - Patient Visit Discovery (`Issue #778`)

- Ngay: 2026-11-14
- Trang thai: accepted
- Lane: high-risk (du lieu benh nhan + phan quyen nguoi than)
- Lien quan: Decision `0040` (view charges), `0041` (QR), `0042` (webhook).

## 1. Boi canh

Benh nhan chi lay duoc `visitCode` qua `GET /api/v1/appointments/{id}/queue-status` sau check-in. Walk-in khong co appointment nen khong co duong nao. DTO appointment detail/list khong chua `visitId`/`visitCode`. `GET /visits/{idOrCode}/charges` da co nhung can biet ma truoc.

## 2. Quyet dinh

Them `GET /api/v1/patients/me/visits`:

- `[Authorize(Roles = "PATIENT")]`. Khong mo rong cho staff.
- Ownership dung `VisitPaymentAccess.LoadOwnedPatientIdsAsync`: ho so cua chinh user + family link `VERIFIED`, chua revoke, con hieu luc. Cung pham vi voi `/charges`.
- User khong ton tai / khong active / dang khoa → `401`; role khong phai PATIENT → `403`.
- Sort `checkedInAt DESC`, tie-break `id DESC`.
- `limit` mac dinh 10, clamp `1..20`. Lay toi da 20 ung vien truoc khi loc de van du trang.
- Tra `visitId`, `visitCode`, `visitStatus`, `checkedInAt`, `completedAt`, `hospitalName`, `paymentStatus`, `paymentCode`.
- Khong tra so tien hay settlement. `GET /charges` van la nguon duy nhat; danh sach chi de dieu huong.
- `paymentStatus`/`paymentCode` lay tu dong `payments` co `id` lon nhat cua visit, khong gia dinh `PENDING`.
- Loai visit ma tap hospital khong dung mot phan tu. Ly do: `GET /charges` tra `409` cho cung dieu kien, nen hien thi se dan den man hinh khong mo duoc.

## 3. Hieu nang

`VisitHospitalResolver` duoc mo rong thanh `CollectManyAsync` cho ca trang: appointment → slot → service/doctor, walk-in audit, room, assigned doctor, checked-in staff, queue service point. So truy van co dinh, khong N+1. `CollectAsync` (mot visit) goi lai batch de chi co mot quy tac.

`VisitWalkInAudits.LoadManyAsync` gom audit cua nhieu visit trong mot truy van, giu thu tu `id` giam dan.

## 4. Gioi han

- Walk-in chi xuat hien neu `visit.patient_id` tro toi ho so ma tai khoan dang nhap so huu hoac duoc family link cho phep. Lien ket danh tinh cho benh nhan walk-in chua co tai khoan nam ngoai scope.
- Khong phan trang sau 20 ban ghi; chua co nhu cau lich su dai.
- Khong thay doi schema, khong migration.

## 5. He qua

- FE (#159) them trang "Luot kham cua toi" goi endpoint nay, moi item tro toi `/payments/visits/{visitCode}`.
- CTA o trang hang doi giu nguyen (loi tat), khong con la duong duy nhat.
