# 0046 — Patient-owned prescription view (`#158` / `M3-050` / UC21)

- Ngày: 2026-11-16
- Trạng thái: accepted
- Lane: high-risk

## Context

UC21 cho phép bệnh nhân xem thuốc đã được kê và hướng dẫn sử dụng. Dữ liệu là PHI; API nhà thuốc hiện có dùng phạm vi bệnh viện và vai trò `PHARMACY_STAFF`, không phù hợp để tái sử dụng cho bệnh nhân. Schema v4.1 đã có đầy đủ `prescriptions`, `prescription_items`, `medicines`, `visits`; không cần migration.

## Decision

1. Expose `GET /api/v1/patients/me/prescriptions` (paged list) and `GET /api/v1/patients/me/prescriptions/{idOrCode}` (detail) on `PatientsController`.
2. Layer 1 requires `[Authorize(Roles = "PATIENT")]`. Layer 2 resolves the active, unlocked JWT user and the single active patient profile linked by `patients.user_id`; the client never supplies a patient id.
3. Return only `ISSUED`, `PREPARING`, `READY`, or `DISPENSED` prescriptions. Hide drafts, cancelled/superseded data, prescriptions replaced by a visible replacement, and records with incomplete medicine references.
4. Detail lookup accepts positive numeric id or a normalized prescription code. Unknown and foreign prescriptions share a 404 response to prevent enumeration.
5. Detail returns persisted medicine identity, quantity, dose, schedule, frequency, duration, route, and instructions. It does not return contact, identity, address, stock, batch, or pricing data.
6. Successful list/detail reads write `PATIENT_PRESCRIPTIONS_VIEWED` / `PATIENT_PRESCRIPTION_VIEWED` audit records without clinical text or patient PII.

## Consequences

- Patient access is isolated from pharmacy hospital-scoping and cannot reuse a client-provided patient identifier.
- Family-member prescription access is intentionally out of scope because issue #158 only authorizes the patient's linked self profile; widening PHI access requires an explicit contract.
- No database or public pharmacy contract changes.
