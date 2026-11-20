# 0046 — Deactivate Staff Account Contract

## Status

Accepted — 2026-11-17. Strict dependency policy explicitly confirmed by the user.

## Context

Issue #232 requires SYSTEM_ADMIN to deactivate a Staff account after validating authorization, ownership, operational dependencies, data integrity, and consistent errors. The schema already separates login (`users`), employment (`staff`), doctor availability (`doctors`), refresh sessions, and audit logs.

## Decision

`PATCH /api/v1/admin/staff/{staffId}/deactivate` requires a 5–500 character reason.

Authorization uses two layers:

1. Controller requires JWT role `SYSTEM_ADMIN`.
2. Service revalidates active/unlocked actor, database role `SYSTEM_ADMIN`, effective `USER_MANAGE`, active Staff profile when present, and rejects self-deactivation.

For doctor-linked Staff, reject with `409` while any of these dependencies remain:

- active visit (`WAITING`, `CALLED`, `IN_PROGRESS`, `IN_CONSULTATION`);
- future active examination slot (not `CANCELLED`, `COMPLETED`, or `INACTIVE`);
- active queue service point.

On success, one `SaveChanges` atomically sets `users.status = INACTIVE`, `staff.status = INACTIVE`, and `doctors.is_active = false` when present; revokes all unexpired refresh sessions; and appends `STAFF_ACCOUNT_DEACTIVATED` audit data. Role assignments and historical references are retained.

## Consequences

- Existing access tokens remain cryptographically valid until expiry; downstream service-level account-status checks continue to deny sensitive operations. Refresh renewal is immediately denied by session revocation and inactive account status.
- Operations must reassign or close doctor work before deactivation.
- No schema migration is required.
