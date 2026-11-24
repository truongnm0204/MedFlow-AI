# 0048 - Auth Proxy Alignment, Atomic Refresh Rotation & Multi-Tab Session Sync

- Ngay: 2026-11-24
- Trang thai: accepted
- Lane: high-risk (Authentication / Session / Rate Limiting / PHI Cache)
- Lien quan: `docs/system-architecture.md`, `.claude/rules/authorization-two-layers.md`, `.claude/rules/api-contract-and-errors.md`

## 1. Hien trang & Van de (da xac minh)

1. **Xung dot giao thuc Vite Proxy va HTTPS Redirection (P0):**
   - `vite.config.ts` proxy `/api` sang `http://localhost:5168`, trong khi `Program.cs` bat `app.UseHttpsRedirection()` vo dieu kien.
   - Khi backend chay profile `https` (`https://localhost:7063;http://localhost:5168`), moi request `/api/v1/*` nhan `307 Temporary Redirect` sang `https://localhost:7063`.
   - Theo chuan WHATWG Fetch, trinh duyet tu dong xoa header `Authorization: Bearer ...` khi redirect khac origin (`http://localhost:5173` -> `https://localhost:7063`). `POST /auth/login` va `POST /auth/refresh` van thanh cong vi gui du lieu qua JSON body, nhung moi API co `[Authorize]` tra `401 Unauthorized`.

2. **Non-atomic Refresh Token Rotation trong `AuthSessionService.RotateAsync` (P0):**
   - `GenerateAsync` insert va commit session moi vao `auth_sessions` **truoc** khi `ExecuteUpdateAsync` thu hoi session cu thanh cong. Neu thua cuoc dua dong thoi hoac gap ngoai le giua chung, session moi bi bo roi (orphan active session).

3. **Axios Interceptor logout sai khi gap loi tam thoi, thieu single-flight lien tab & race khi logout giua chung (P0/P1):**
   - `src/shared/lib/axios.ts` goi `clearAuth()` va redirect `/login` voi **moi** loi tu `/auth/refresh` (bao gom `429 Too Many Requests`, `5xx`, timeout, mat mang).
   - `isAuthEndpoint` chua bao gom `/auth/logout`.
   - `refreshPromise` chi chong goi trung trong cung 1 tab; 2 tab cung gap `401` gui cung 1 `refreshToken` va kich hoat `REFRESH_REUSE_DETECTED` -> thu hoi toan bo phien cua user.
   - Chua co co che ton trong `Retry-After` hoac chan response `/auth/refresh` den muon khoi phuc phien da logout / ghi de phien tai khoan khac.

4. **Rate Limiter dung chung bo dem toan cuc & tin mu `X-Forwarded-For` (P1):**
   - Policy `"auth"` dung `AddFixedWindowLimiter` khong phan vung (`PermitLimit = 5`/phut cho toan server) va dung chung cho `login`, `register`, `refresh`.
   - `AuthController.GetIp()` doc truc tiep header `X-Forwarded-For` tu request ma khong qua `ForwardedHeadersMiddleware`.

5. **Lech trang thai Zustand giua nhieu tab trinh duyet (P1):**
   - `useAuthStore` trong `auth.store.ts` chua dong bo in-memory state khi tab khac login, refresh token, cap nhat profile hoac logout, va chua xoa React Query PHI cache tren cac tab con lai khi logout.

## 2. Quyet dinh

### 2.1. Thong nhat giao thuc Proxy & HTTPS Redirection
- Trong `Program.cs`:
  - Cau hinh `UseForwardedHeaders()` dung thu tu dau pipeline va xoa `KnownProxies`/`KnownIPNetworks` mac dinh — chi tin tuong cac proxy duoc khai bao tuong minh trong `ReverseProxy:KnownProxies` / `ReverseProxy:KnownIPNetworks`.
  - Tat `app.UseHttpsRedirection()` trong `Development` va `Test`; bat buoc `UseHsts()` + `UseHttpsRedirection()` trong `Production` (va cac moi truong non-dev/non-test).
- Trong `vite.config.ts`:
  - Giu `http://localhost:5168` lam mac dinh, bo sung `VITE_API_PROXY_TARGET` ho tro tuy chon HTTPS (`https://localhost:7063`).

### 2.2. Atomic Refresh Token Rotation (1 DB Transaction, khong Grace Period)
- Trong `AuthSessionService.RotateAsync`:
  - Sinh `newRawToken` + `newTokenHash` tren RAM truoc; khong `INSERT` truoc khi thang `ExecuteUpdateAsync`.
  - Boc toan bo kiem tra, thu hoi session cu (`ExecuteUpdateAsync` voi dieu kien `id = @id AND revoked_at IS NULL AND expires_at > @now`) va `INSERT` session moi trong 1 DB transaction duy nhat. Neu bat ky buoc nao truoc commit gap loi, transaction rollback toan bo — khong bao gio tao orphan session.
  - Giu nguyen chinh sach `RevokeAllForUserAsync` va audit `REFRESH_REUSE_DETECTED` khi phat hien reuse (khong bo sung grace period lam suy yeu reuse detection; bao toan tuyet doi contract cua `AuthRefreshTests.cs`).

### 2.3. Tach Rate Limit Policy theo Endpoint & Trusted Client IP
- `AuthController.GetIp()` chi lay `HttpContext.Connection.RemoteIpAddress` sau `UseForwardedHeaders()`.
- Tach Rate Limiter thanh 5 policy phan vung theo Client IP (`auth-login` 10/phut, `auth-register` 5/phut, `auth-refresh` 30/phut, `otp-verify` 10/phut, `otp-resend` 5/phut) va tra `ProblemDetails` + header `Retry-After` khi `429`.

### 2.4. Axios 401: Web Locks, Phan loai loi, `Retry-After` & Chong khoi phuc phien cu
- Giu `refreshPromise` trong cung tab ket hop `navigator.locks.request('medflow:auth:refresh', ...)` giua cac tab (kem fallback lease lock tren `localStorage`).
- Sau khi lay lock, doc lai `localStorage`: neu tab khac da rotate sang token moi thi dung ngay token moi ma khong goi `/auth/refresh` lan nua.
- Chi `clearAuth()` + redirect `/login?returnUrl=<pathname + search>` khi `/auth/refresh` tra `401`/`403` hoac mat han `refreshToken`; tuyet doi khong logout khi gap `429`, `5xx`, timeout hoac network error.
- Ton trong header `Retry-After` tren `429` va gioi han so lan thu lai tam thoi (`MAX_CONSECUTIVE_TRANSIENT_FAILURES = 3`, moi request goc chi retry 1 lan).
- Kiem tra `sessionEpoch`, `expectedUserId` va `expectedRefreshToken` (CAS) truoc khi ghi ket qua refresh de response den muon khong the khoi phuc phien da logout hoac ghi de phien cua tai khoan khac.

### 2.5. Dong bo Zustand & Don sach PHI Cache giua cac tab
- Dong bo qua `storage` event + `BroadcastChannel` voi co `isApplyingExternalSync` ngan ghi nguoc `localStorage` (tranh vong lap su kien); tu dong goi `queryClient.clear()` khi nhan su kien logout tu tab khac.

## 3. He qua (Consequences)

- **Duoc:** Triet tieu loi `307 -> xoa Authorization -> 401 -> 429 -> logout`, dam bao ACID cho refresh rotation, bao ve phien lam viec lam sang khi mang chap chon, chong race giua logout va in-flight refresh, dong bo an toan da tab.
- **Rui ro con lai (P2):** Danh gia chuyen `refreshToken` sang `HttpOnly; Secure` cookie + CSRF trong mot ADR rieng de khong pha vo API contract hien tai.
