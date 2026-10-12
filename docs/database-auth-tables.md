# Auth DB Sheet — MedFlow (schema `medflow`, PostgreSQL 18)

DB: `medflow_db` @ localhost:5432. Test DB: `medflow_test` (cùng DDL + `001_create_refresh_tokens.sql`).

## Bảng dùng cho Auth (9 bảng)

### users
| Cột | Kiểu | Ghi chú |
|---|---|---|
| id | uuid PK | |
| username | varchar | unique — dùng = email |
| email | varchar | unique lower (`uq_users_email_lower`) |
| password_hash | varchar | PasswordHasher PBKDF2 |
| auth_provider | varchar | `LOCAL` |
| account_status | varchar | `PENDING` → `ACTIVE` (LOCKED/DEACTIVATED) |
| email_verified_at | timestamptz | set khi verify OTP |
| failed_login_attempts | int | reset về 0 khi login OK |
| locked_until | timestamptz | +15min sau 5 lần sai |
| last_login_at | timestamptz | |
| created_at / updated_at | timestamptz | |

### roles / user_roles / role_permissions
- `roles`: `id`, `code` (`PATIENT`, `DOCTOR`, `ADMIN`... 7 roles đã seed), `name`.
- `user_roles`: `user_id` + `role_id` (FK).
- `role_permissions`: `role_id` + `permission_id` → `permissions(id, code, ...)`.

### patients (tạo cùng register)
| Cột | Ghi chú |
|---|---|
| id uuid PK, user_id FK→users | 1-1 |
| patient_code varchar unique | `PAT-yyyyMMdd-XXXX` |
| full_name, date_of birth(date), gender, phone, email | từ register form |
| status varchar | `ACTIVE` |

### auth_verification_challenges (OTP)
| Cột | Ghi chú |
|---|---|
| id uuid PK, user_id FK | |
| purpose | `REGISTER_EMAIL` (CHECK: REGISTER_EMAIL/CHANGE_EMAIL/CHANGE_PHONE/PASSWORD_RESET) |
| channel | `EMAIL` |
| recipient_fingerprint | bytea 32 = HMAC-SHA256(email) |
| secret_hash | bytea 32 = HMAC-SHA256(otp) |
| status | `PENDING`→`VERIFIED`/`EXPIRED`/`LOCKED`/`REVOKED` |
| attempts / max_attempts | mặc định 0 / 5 |
| expires_at, verified_at, created_at | TTL 5 phút |

### audit_logs
`id`, `actor_user_id`, `action` (`REGISTER`,`LOGIN_SUCCESS`,`LOGIN_FAILED`,`OTP_VERIFY`,`OTP_RESEND`,`LOGOUT`,`REFRESH_REUSE_DETECTED`,`EMAIL_SEND_FAILED`), `entity_type` (`USER`), `entity_id`, `new_value` jsonb, `ip_address`, `created_at`.

### refresh_tokens (MỚI — migration `001_create_refresh_tokens.sql`, không có trong DDL v1.2)
| Cột | Ghi chú |
|---|---|
| id uuid PK, user_id FK cascade | |
| token_hash varchar(128) unique | SHA-256 hex |
| expires_at / revoked_at | TTL 7 ngày; rotate → set revoked_at + replaced_by_token_hash |
| created_by_ip / revoked_by_ip | |

## Flow nhanh
- Register: user PENDING + patient + user_roles(PATIENT) + challenge PENDING + audit — 1 transaction.
- Verify: challenge VERIFIED → user ACTIVE + email_verified_at.
- Login: check ACTIVE + locked_until → JWT 60min + refresh row.
- Refresh: hash lookup → rotate (revoke cũ, insert mới); reuse revoked → revoke family + audit.
- Logout: revoke token (idempotent).
