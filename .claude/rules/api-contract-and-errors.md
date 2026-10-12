# API Contract & Error Handling

Trich tu auth module thuc te.

## Backend

- Controller thin: chi bind DTO + goi `IXxxService`. Khong business logic, khong try/catch.
- Business error = typed domain exception (`ValidationException` 400, `NotFoundException` 404, `UnauthorizedAccessException` 401) → `ExceptionMiddleware` map sang `ProblemDetails` JSON `{ type, title, status, detail }`.
- Chong enumeration: login sai user/password → cung message "Sai thong tin dang nhap"; resend OTP → generic response cho ca email ton tai/khong.
- Route prefix `api/v1/` + kebab-case. Version trong path, khong header.
- Audit: moi su kien nhay cam ghi `audit_logs` (actor_user_id, action, entity_type, entity_id, new_value jsonb, ip_address). IP lay `X-Forwarded-For` → `IPAddress.TryParse`.

## Frontend

- Moi axios call di qua `apiClient` trong `src/shared/lib/axios.ts` — interceptors xu ly 401 refresh + error toast. Khong scatter try/catch o call site.
- DTO types derive tu Zod schema khi co the: `type LoginInput = z.infer<typeof loginSchema>`.
- API file `<resource>.api.ts` chi chua axios calls typed in/out — component khong bao gio goi axios truc tiep.
