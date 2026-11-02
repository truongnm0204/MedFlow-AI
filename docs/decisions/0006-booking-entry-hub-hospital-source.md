# ADR-0006 — Booking Entry Hub: hospital source & downstream `hospitalId`

**Status:** Accepted
**Date:** 2026-11-01
**Issue:** #619 (FE booking entry hub)

## Context

Step 1 of the booking entry screen (`/booking`) lists hospitals (name, address, reception status). The backend today has **no** `GET /api/v1/hospitals`, no `Hospital` entity, and doctor/service search DTOs have **no** `hospitalId` filter. A `hospitals` DB table exists (`id bigint, hospital_code, name, address, is_active`) with 1 seed row.

FE convention for not-yet-existing endpoints (established by `doctor-search.api.ts`, `service-search.api.ts`): call the real `apiClient` endpoint, fall back to a committed mock on 404/network, and in Vitest (`MODE==='test'`) go straight to mock unless `apiClient.get` is spied.

## Decision

1. **FE consumes a future `GET /api/v1/hospitals`** returning a flat `HospitalDto[]` `{ id, hospitalCode, name, address, isActive }`. `fetchHospitalsApi()` maps DTO → domain `Hospital` (null-safe) and falls back to `MOCK_HOSPITALS` on 404/network. Non-404 HTTP errors rethrow so the UI shows error+retry.
2. **`hospitalId` is carried downstream only via URL query param + `useBookingStore`** (`selectedHospitalId`, `bookingMode`). `/doctors?hospitalId=…` and `/services?hospitalId=…` pages are **not** changed — the backend can't filter by hospital yet.
3. **Backend endpoint is a separate issue.** It needs `Hospital` entity + EF Configuration + query + seed. When it lands, the FE works unchanged; if the endpoint wraps results, unwrap `data.items ?? data.data ?? data` in `fetchHospitalsApi` only.
4. Inactive hospitals (`isActive=false`) render **disabled, not hidden**, so the reception status required by the issue is visible.

## Consequences

- **+** FE ships against a stable contract; backend work unblocks independently.
- **+** Mock fallback keeps `/booking` functional in dev/test without backend.
- **−** `hospitalId` is informational until backend filtering exists; a user could hand-edit the URL — harmless, downstream pages ignore unknown params.
- **−** If backend ships a different DTO shape, only `fetchHospitalsApi`'s mapper changes.
