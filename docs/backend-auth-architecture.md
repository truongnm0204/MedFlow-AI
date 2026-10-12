# Backend — Cau truc code + Authentication

Ap dung cho `medflow-ai-backend/` — .NET 10, ASP.NET Core Web API, EF Core 10, PostgreSQL, N-Layer.

## 1. Cau truc solution

```
medflow-ai-backend/
├── src/
│   ├── MedFlow.API/            Presentation: controllers, middleware, Program.cs
│   ├── MedFlow.Application/    Use-case: services, DTOs, interfaces (khong reference Infrastructure/EF)
│   ├── MedFlow.Domain/         Entities, enums, exceptions — zero dependency
│   └── MedFlow.Infrastructure/ EF Core DbContext, configurations, Identity (JWT/refresh), Integrations (SMTP)
├── tests/
│   ├── MedFlow.UnitTests/      Test nghiep vu thuan (OtpHasher, PasswordValidator, UserEntity)
│   └── MedFlow.IntegrationTests/ Test end-to-end qua HTTP (TestWebAppFactory + Postgres test DB)
└── database/migrations/        SQL DDL cho bang moi (refresh_tokens)
```

Luong phu thuoc (dependency rule):

```
API → Application → Domain
API → Infrastructure → Application (chi qua interface)
Infrastructure → Domain
```

`MedFlow.Application` KHONG using `Microsoft.EntityFrameworkCore` — no chi goi `IApplicationDbContext` do `MedFlow.Infrastructure` implement. Day la diem mau chot giu Domain/Application doc lap framework.

## 2. Cau truc tung layer

### 2.1 `MedFlow.Domain`

```
Domain/
├── Entities/         POCO voi private set + static factory/behavior
├── Enums/            AccountStatus, ChallengeStatus, ChallengePurpose, ChallengeChannel
└── Exceptions/       DomainException, ValidationException, NotFoundException
```

Vi du `User`:

```csharp
public sealed class User
{
    public Guid Id { get; private set; }
    public string Username { get; private set; }
    public string Email { get; private set; }
    public string? PasswordHash { get; private set; }
    public AccountStatus AccountStatus { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTime? LockedUntil { get; private set; }
    public Patient? Patient { get; private set; }
    public ICollection<UserRole> UserRoles { get; private set; } = [];

    private User() { } // EF Core

    public static User CreatePatientAccount(string email, string passwordHash) => new()
    {
        Id = Guid.NewGuid(),
        Username = email.ToLowerInvariant(),
        Email = email.ToLowerInvariant(),
        PasswordHash = passwordHash,
        AccountStatus = AccountStatus.PENDING,
        AuthProvider = "LOCAL",
    };

    public void Activate() => AccountStatus = AccountStatus.ACTIVE;
    public void RecordLoginFailure(int maxAttempts, TimeSpan lockDuration) { /* lock logic */ }
    public void RecordLoginSuccess() { FailedLoginAttempts = 0; LockedUntil = null; LastLoginAt = DateTime.UtcNow; }
}
```

Ly do: entity tu bao ve invariant — service khong the set `AccountStatus` truc tiep, phai qua `Activate()`/`RecordLogin*`. Giam bug logic rai rac.

### 2.2 `MedFlow.Application`

```
Application/
├── DependencyInjection.cs      AddApplication() — dang ky scoped services
├── DTOs/Auth/                  LoginRequest/Response, RegisterRequest/Response, Refresh*, VerifyOtp*, UserDto
├── Interfaces/
│   ├── Persistence/            IApplicationDbContext (DbSet + SaveChanges + BeginTransaction)
│   └── Services/               IJwtTokenService, IRefreshTokenService, IEmailSender
└── Services/Auth/
    ├── IAuthService.cs         Contract controller goi
    ├── AuthService.cs          Orchestrate: register/login/refresh/logout/getme
    ├── AuthOtpService.cs       OTP: verify + resend (tach de file <200 dong)
    ├── AuthCryptoHelpers.cs    Static: HMAC-SHA256 OTP, fingerprint, FixedTimeEquals
    └── OtpEmailBuilder.cs      Static: build HTML + goi IEmailSender + audit fail
```

Giao dien chinh:

```csharp
public interface IAuthService
{
    Task<RegisterResponse> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken ct);
    Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct);
    Task<RefreshResponse> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken ct);
    Task LogoutAsync(Guid userId, string refreshToken, string? ipAddress, CancellationToken ct);
    Task<VerifyOtpResponse> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken ct);
    Task<ResendOtpResponse> ResendOtpAsync(ResendOtpRequest request, CancellationToken ct);
    Task<UserDto> GetMeAsync(Guid userId, CancellationToken ct);
}
```

`AuthService` giu nhiem vu orchestrate: doc config, goi domain methods, dieu phoi `IJwtTokenService`/`IRefreshTokenService`/`AuthOtpService`, ghi `AuditLog`. Logic nhay cam (HMAC, OTP gen, email HTML) tach sang `AuthCryptoHelpers`/`OtpEmailBuilder` de test doc lap va giu `AuthService` <200 dong.

### 2.3 `MedFlow.Infrastructure`

```
Infrastructure/
├── DependencyInjection.cs          AddInfrastructure(config) — DbContext + repositories + services
├── Identity/
│   ├── JwtTokenService.cs          SymmetricSecurityKey HMAC-SHA256, claims sub/email/jti/roles
│   └── RefreshTokenService.cs      Opaque token 64B, SHA-256 hash, rotate + reuse detection
├── Integrations/Notification/
│   └── SmtpEmailSender.cs          MailKit → Gmail smtp.gmail.com:587 STARTTLS
└── Persistence/
    ├── MedFlowDbContext.cs         HasDefaultSchema("medflow") + ApplyConfigurationsFromAssembly
    └── Configurations/             1 file per entity: UserConfiguration, PatientConfiguration,
                                    RefreshTokenConfiguration, AuthVerificationChallengeConfiguration, ...
```

`IApplicationDbContext` la "cau noi" duy nhat Application can:

```csharp
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Patient> Patients { get; }
    DbSet<Role> Roles { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<AuthVerificationChallenge> AuthVerificationChallenges { get; }
    DbSet<AuditLog> AuditLogs { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
```

`MedFlowDbContext : DbContext, IApplicationDbContext` — EF Core implement san, khong can wrapper.

### 2.4 `MedFlow.API`

```
API/
├── Controllers/AuthController.cs       [Route("api/v1/auth")] — 7 endpoint, thin, chi bind + goi IAuthService
├── Middleware/ExceptionMiddleware.cs   Bat DomainException/ValidationException → ProblemDetails
├── Program.cs                          AddApplication() + AddInfrastructure(config) + JWT Bearer + rate limit
├── appsettings.json                    Base config
├── appsettings.Development.json        Conn string placeholder DEV_LOCAL_ONLY (override bang user-secrets)
└── appsettings.Test.json               Jwt secret test + Smtp localhost — KHONG chua ConnectionStrings
```

## 3. Authentication flow — chi tiet

### 3.1 Register (`POST /api/v1/auth/register`)

```
Client ──> AuthController.Register(RegisterRequest)
           └─> IAuthService.RegisterAsync(req, ip)
                ├─ Check email ton tai? → ValidationException 400
                ├─ Load role PATIENT (seeded)
                ├─ GeneratePatientCodeAsync → PAT-yyyyMMdd-XXXX (retry neu trung)
                ├─ AuthCryptoHelpers.GenerateOtp() → 6 digits
                ├─ ComputeHmacHex(otp, Auth:OtpSecret) → SecretHash
                ├─ ComputeHmacBytes(email, Auth:OtpSecret) → RecipientFingerprint
                └─ Transaction:
                     ├─ User.CreatePatientAccount → PENDING
                     ├─ Patient { UserId, FullName, DOB, Gender, Phone, PatientCode }
                     ├─ UserRole { UserId, PATIENT }
                     ├─ AuthVerificationChallenge { PENDING, SecretHash, Fingerprint, ExpiresAt=+5min }
                     └─ AuditLog REGISTER
                └─> AuthOtpService.SendOtpAndAuditAsync
                     └─ OtpEmailBuilder.BuildHtml → IEmailSender.SendAsync
                          └─ SmtpEmailSender (MailKit → Gmail)
                     (fail → audit EMAIL_SEND_FAILED, khong throw)
           <── 200 RegisterResponse { userId, email }
```

Diem quan trong:
- Password hash bang `PasswordHasher<User>` (PBKDF2 + salt).
- OTP KHONG luu plain — chi `HMAC-SHA256(otp, OtpSecret)` vao `secret_hash` (bytea 32). Neu DB leak, attacker khong doc duoc OTP.
- `recipient_fingerprint` = HMAC(email) — check OTP gui dung email ma khong can luu email trong challenge.
- Transaction bao 4 writes (user + patient + role + challenge + audit) — atomic.

### 3.2 Verify OTP (`POST /api/v1/auth/verify-otp`)

```
VerifyOtpRequest { Email, Otp }
  └─> AuthOtpService.VerifyOtpAsync
       ├─ Tim user theo email (lowercase)
       ├─ Neu user.AccountStatus == ACTIVE → throw "da duoc kich hoat" (chong verify lai)
       ├─ Tim challenge PENDING moi nhat purpose=REGISTER_EMAIL
       ├─ Check ExpiresAt → qua han → challenge.Status=EXPIRED → throw
       ├─ Check Attempts >= MaxAttempts(5) → Status=LOCKED → throw
       ├─ ComputeHmacHex(inputOtp) + FixedTimeEquals(inputHash, challenge.SecretHash)
       │    └─ sai → Attempts++ → throw "OTP khong dung"
       └─ Dung → challenge.Status=VERIFIED + VerifiedAt
                 user.Activate() + EmailVerifiedAt=now
                 AuditLog OTP_VERIFY
```

`FixedTimeEquals` dung `CryptographicOperations.FixedTimeEquals` de chong timing attack — khong return som khi byte dau sai.

### 3.3 Resend OTP (`POST /api/v1/auth/resend-otp`)

- Tra generic `ResendOtpResponse()` cho ca user ton tai/khong — chong enumeration.
- Throttle 45 s: neu challenge PENDING moi nhat `CreatedAt > now - 45s` → throw "doi truoc khi gui lai".
- Neu ACTIVE → throw.
- Revoke challenge cu (`Status=REVOKED`) + insert challenge moi PENDING + gui email.

### 3.4 Login (`POST /api/v1/auth/login`)

```
LoginRequest { Identifier, Password }
  └─> AuthService.LoginAsync
       ├─ Tim user by Username OR Email (ca hai = email hien tai)
       │    .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
       │    .Include(u => u.Patient)          ← lay fullName + patientCode 1 query
       ├─ user null hoac !ACTIVE → throw "Sai thong tin dang nhap" (chong enumeration)
       ├─ LockedUntil > now → throw "dang bi khoa"
       ├─ _passwordHasher.VerifyHashedPassword(user.PasswordHash, input)
       │    └─ Fail → user.RecordLoginFailure(5, 15min)
       │              AuditLog LOGIN_FAILED + save → throw
       └─ OK → user.RecordLoginSuccess()
               roles = UserRoles → JwtTokenService.GenerateAccessToken(user, roles)
               refresh = RefreshTokenService.GenerateAsync(userId, ip)
               AuditLog LOGIN_SUCCESS
               return { accessToken(60min), refreshToken(7d), userDto }
```

### 3.5 Refresh (`POST /api/v1/auth/refresh`)

```
RefreshRequest { RefreshToken }
  └─> RefreshTokenService.RotateAsync(rawToken, ip)
       ├─ hash = SHA256(rawToken) → tim RefreshToken by TokenHash
       ├─ Neu khong tim hoac het han → return { Success=false }
       ├─ Neu RevokedAt != null → REUSE → RevokeAllForUserAsync → return { ReuseDetected=true }
       ├─ GenerateAsync → new RefreshToken (chua save)
       └─ ExecuteUpdateAsync:
            WHERE Id=old.Id AND RevokedAt IS NULL
            SET RevokedAt=now, RevokedByIp=ip, ReplacedByTokenHash=new.Hash
            └─ updated=0 → concurrent rotate da thang → RevokeAll + ReuseDetected
       return { RawToken=newToken, Entity=new, User }
  └─> AuthService.RefreshAsync
       ├─ ReuseDetected → AuditLog REFRESH_REUSE_DETECTED → throw 401
       └─ OK → new accessToken (60min)
       return { accessToken, refreshToken (rotate) }
```

Diem bao mat:
- Chi luu `SHA-256(token)` trong DB — token raw khong bao gio persist.
- Reuse detection: token cu bi goi lai → coi nhu bi danh → revoke toan bo family cua user → force re-login.
- `ExecuteUpdateAsync` atomic o DB level — giai quyet race 2 request refresh song song (code-review finding #4).

### 3.6 Logout (`POST /api/v1/auth/logout`)

- `RefreshTokenService.RevokeAsync(rawToken, ip)` — idempotent (token da revoke thi bo qua).
- Audit `LOGOUT`. Access token van con han toi 60min — muon revoke ngay can blacklist (deferred, plan khong yeu cau).

### 3.7 GetMe (`GET /api/v1/auth/me`)

- `[Authorize]` → `sub` claim → `userId`.
- Query `.Include(Patient).Include(UserRoles.Role)` → tra `UserDto { Id, Username, Email, FullName, Roles[], PatientCode }`.

## 4. Cau hinh quan trong

| Key | Vi tri | Ghi chu |
|-----|--------|---------|
| `ConnectionStrings:DefaultConnection` | appsettings / env / user-secrets | `appsettings.Development.json` chi la placeholder `DEV_LOCAL_ONLY`; prod/test override qua env `ConnectionStrings__DefaultConnection` |
| `Jwt:Secret` | user-secrets / env | Toi thieu 32 ky tu cho HMAC-SHA256 |
| `Jwt:AccessTokenExpiryMinutes` | appsettings | 60 |
| `Jwt:RefreshTokenExpiryDays` | appsettings | 7 |
| `Auth:OtpSecret` | user-secrets / env | HMAC key rieng cho OTP, khac Jwt:Secret |
| `Smtp:Host/Port/Username/Password` | user-secrets | Gmail smtp.gmail.com:587; Password = App Password (16 ky tu) |

## 5. Exception middleware

```
DomainException (ValidationException 400, NotFoundException 404, UnauthorizedAccessException 401)
  → ExceptionMiddleware → ProblemDetails JSON { type, title, status, detail }
```

Controller khong can try/catch — moi business error la typed exception, middleware map sang HTTP status dung.

## 6. Rate limit

Program.cs (chi bat o env != Test):
- Policy `auth`: 5 req/min/IP cho `/auth/login`, `/auth/register`, `/auth/refresh`, `/auth/logout`, `/auth/me`.
- Policy `otp`: 10 req/min/IP cho `/auth/verify-otp`, `/auth/resend-otp`.

## 7. Audit

Moi su kien nhay cam ghi `audit_logs`: `REGISTER`, `LOGIN_SUCCESS`, `LOGIN_FAILED`, `OTP_VERIFY`, `OTP_RESEND`, `LOGOUT`, `REFRESH_REUSE_DETECTED`, `EMAIL_SEND_FAILED`. Fields: `actor_user_id`, `action`, `entity_type`, `entity_id`, `new_value` (jsonb), `ip_address`, `created_at`. IP lay `X-Forwarded-For` → `IPAddress.TryParse` (khong throw).

## 8. Test

- `MedFlow.UnitTests`: OtpHasher (HMAC + FixedTimeEquals), PasswordValidator, UserEntity (lock/activate/patient code).
- `MedFlow.IntegrationTests`: `TestWebAppFactory` — `UseEnvironment("Test")`, env `ConnectionStrings__DefaultConnection` tro toi `medflow_test` (doc `MEDFLOW_TEST_DB` hoac default), swap `IEmailSender` → `FakeEmailSender` (extract OTP tu HTML). 25 test cover: register flow, verify OTP, login + lock, refresh rotate + reuse + concurrent.

## 9. Tai lieu lien quan

- `docs/system-architecture.md` — overview ca frontend + backend
- `docs/backend-code-standards.md` — naming, layer rules, quality gates
- `docs/project-changelog.md` — lich su thay doi, fixes tu code review
- `plans/reports/code-review-2026-10-12-auth-homepage.md` — full findings + fixes
- `docs/database-auth-tables.md` — schema chi tiet 9 bang auth
