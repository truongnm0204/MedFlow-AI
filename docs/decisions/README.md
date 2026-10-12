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
