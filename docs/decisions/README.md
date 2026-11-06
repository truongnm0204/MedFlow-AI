# Architecture Decision Records (ADR)

Format: `NNNN-short-slug.md` (NNNN = so thu tu tang dan).

Moi file ghi 1 quyet dinh kien truc/co y nghia lau dai. Khong phai changelog — chi quyet dinh + ly do + trade-off.

## Template

```markdown
# NNNN — Tieu de quyet dinh

- Ngay: yyyy-mm-dd
- Trang thai: proposed | accepted | superseded by NNNN
- Lane: normal | high-risk

## Context
Van de/dieu kien dan den quyet dinh.

## Decision
Quyet dinh gi — cu the.

## Consequences
- Duoc: ...
- Mat: ...
- Rui ro con lai: ...
```

## Quyet dinh can ADR

- Chon/migration DB, schema approach
- Auth mechanism (JWT strategy, OTP design, token storage)
- External provider integration
- Breaking API contract changes
- Thay doi cau truc layer/module

## Hien co (chua formalize)

- JWT refresh token rotation + reuse detection → xem `docs/backend-auth-architecture.md` (nen chuyen thanh ADR-0001 khi co thoi gian)
- SQL migrations thay EF migrations → `database/migrations/`
- `0013-view-examination-queue-contract.md` — View Examination Queue API contract, two-layer authz, service point grouping & UC28 serving order (`#88` / `M2-036`)
- `0014-early-arrival-safe-backfill-contract.md` — Early Arrival / Safe Backfill command & ExaminationQueueOrderingEngine (`#96` / `M2-044`)
- `0015-outpatient-clinical-priority.md` — Outpatient Clinical Priority (`NORMAL`/`ELDERLY`/`PREGNANT`), `RETURN_CONSULTATION` queue class & 3-tier waiting queue ordering (`#102`)
