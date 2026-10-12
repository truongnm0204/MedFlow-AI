# Development Roadmap — MedFlowAI

Roadmap song cua du an MedFlowAI. Cap nhat khi co phase status change hoac milestone moi.

Legend status: `done` | `in-progress` | `planned` | `blocked`.

## Tong quan

| Phase | Ten | Status | Plan/Report |
|---|---|---|---|
| 0 | Scaffold monorepo + DB schema | done | — |
| 1 | Patient authentication + Homepage | done | [plan](../plans/261012-2037-authen-and-homepage/plan.md) / [review](../plans/reports/code-review-2026-10-12-auth-homepage.md) |
| 2 | Appointment booking | planned | — |
| 3 | Doctor schedule | planned | — |
| 4 | Medical records | planned | — |
| 5 | Admin module | planned | team config `~/.claude/teams/admin-module` |

## Phase 0 — Scaffold + DB (done)

- Monorepo `medflow-ai-backend` (.NET 10 N-Layer) + `medflow-ai-frontend` (React 19 + Vite 8 + Tailwind v4).
- PostgreSQL 18 `medflow_db` @ localhost:5432, schema `medflow` v1.2 — cac bang chinh: `users`, `roles`, `user_roles`, `role_permissions`, `permissions`, `patients`, `auth_verification_challenges`, `audit_logs` + RBAC seed.
- GitHub milestones + labels sync qua `.github/milestones.yml`, `.github/labels.yml`, workflow `sync-labels.yml`.

## Phase 1 — Patient auth + Homepage (done)

Implement plan `plans/261012-2037-authen-and-homepage/` (6 phase-00..05, effort ~28h).

Pham vi chinh:
- 7 endpoint `api/v1/auth/*`: register, verify-otp, resend-otp, login, refresh, logout, me.
- JWT access 60 phut + refresh 7 ngay rotate + reuse detection.
- OTP 6 digits HMAC-SHA256, TTL 5 phut, qua Gmail SMTP (MailKit).
- Brute-force lock 5 attempts / 15 phut; rate limit auth 5/min, otp 10/min.
- Frontend: Zustand `medflow.auth` persist, axios 401→refresh single-flight, 3 trang auth Figma, homepage 7 sections + AppHeader 2 trang thai.
- Migration manual `database/migrations/001_create_refresh_tokens.sql`.

Ket qua:
- Backend 25 unit + 25 integration; frontend 35 tests — tong 85 pass, 0 warnings.
- Code review verdict APPROVE; tat ca findings da fix (xem changelog 2026-10-12).

## Phase 2 — Appointment booking (planned)

- Patient dat lich kham theo chuyen khoa/bac si.
- CRUD `appointments`, `time_slots`.
- Notifications email.

## Phase 3 — Doctor schedule (planned)

- Doctor quan ly lich lam viec, ngay nghi, slot kham.
- Calendar view, sync voi appointments.

## Phase 4 — Medical records (planned)

- Ho so benh an dien tu: chan doan, don thuoc, ket qua xet nghiem.
- Upload file, versioning.

## Phase 5 — Admin module (planned)

- Dashboard admin: users, roles, audit log viewer.
- Team config: `~/.claude/teams/admin-module`.

## Success metrics

- Tests: 85/85 pass (backend 50, frontend 35).
- Build: 0 warnings (NU1902/NU1903/MSB3277 da giai quyet).
- Security: khong commit secrets; Gmail App Password + JWT + OtpSecret trong user-secrets/env var.
- Refresh token atomic rotate (ExecuteUpdateAsync) — chong race condition.

## Known issues / deferred

- `users.updated_at` + `patients.updated_at` chua co trigger update tu dong.
- README cho dev moi can bo sung huong dan `dotnet user-secrets` setup (`Smtp:Password`, `Auth:JwtSecret`, `Auth:OtpSecret`, `ConnectionStrings:DefaultConnection`).
- Refresh token luu `localStorage` (XSS risk chap nhan cho MVP; HttpOnly cookie la enhancement).
