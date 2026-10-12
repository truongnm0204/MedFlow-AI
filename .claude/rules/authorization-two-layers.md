# Authorization: Two Layers (Hard Gate)

Trich tu auth module thuc te (`medflow-ai-backend/src/MedFlow.API`, `MedFlow.Application`).

## Layer 1 — Route/endpoint guard

`[Authorize]` attribute + role claims. Chi chan request chua dang nhap hoac sai role. **Khong du** de bao ve du lieu.

```csharp
[Authorize(Roles = "PATIENT")] // layer 1: da login + dung role
public async Task<IActionResult> GetMedicalRecord(Guid id) { ... }
```

## Layer 2 — Ownership/service-level check

Bat buoc khi request cham du lieu co chu so huu (patient record, appointment, prescription...). Kiem tra `userId` tu `sub` claim so voi owner cua resource — **khong tin client-supplied id**.

```csharp
// trong Service — layer 2
var record = await _ctx.MedicalRecords.FindAsync(id, ct)
    ?? throw new NotFoundException();
if (record.PatientId != currentUserId && !currentUserRoles.Contains("DOCTOR"))
    throw new UnauthorizedAccessException();
```

## Quy tac

- An/hien link tren UI (`guards.tsx`, `sec:authorize`-equivalent) chi la UX — **khong phai phan quyen**.
- Moi endpoint doc/ghi du lieu ca nhan PHAI co layer 2 check trong Service, khong chi `[Authorize]` o controller.
- Test: viet integration test goi endpoint voi id cua user khac → phai 403/404, khong duoc 200.
- POST tra 200 khong chung minh write bi chan — assert tren DB row count (da co trong IntegrationTests).
