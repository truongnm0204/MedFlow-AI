# Impact Analysis Before Edit (GKG)

GKG 0.25.0 da cai (`gkg` tren PATH) — knowledge graph tuong duong GitNexus. MedFlowAI can duoc index truoc khi rule nay co hieu luc.

## Quy tac

1. **Truoc khi edit public method/class/co nhieu callers**: chay GKG query de tim references → bao blast radius. Neu workspace chua index → fallback: dung Explore agent `find_referencing_symbols`.
2. **Neu blast radius HIGH/CRITICAL** (nhieu callers, cham contract/interface dung o nhieu noi): canh bao user truoc khi tiep tuc.
3. **Truoc khi commit**: chay `git status --short` + `git diff --stat` — xem lai toan bo file thay doi, phat hien file lac (artifact, debug leftover).
4. **Khong bao gio rename bang find-and-replace** voi symbol co callers — dung refactor tool hoac trace references truoc.

## Khi nao bat buoc

- Sua signature cua method trong `IAuthService`, `IApplicationDbContext`, hoac interface Application layer bat ky.
- Sua entity/domain method duoc nhieu service goi (`User.RecordLoginFailure`, `RefreshTokenService.RotateAsync`...).
- Doi API contract (DTO field, route path, response shape) — phai check ca frontend `features/*/api/*.api.ts`.
