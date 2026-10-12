# EF Core Migration & Schema Rules

Trich tu `medflow-ai-backend` thuc te.

## Hien trang

- Schema source of truth: `database/medflow_schema_v1_2.sql` + `database/migrations/*.sql` (SQL DDL).
- EF Core `MedFlowDbContext` dung `HasDefaultSchema("medflow")` + `ApplyConfigurationsFromAssembly` — moi entity co 1 `*Configuration` file rieng trong `Infrastructure/Persistence/Configurations/`.
- Khong dung EF migrations hien tai — schema quan ly bang SQL files.

## Quy tac

1. **Khong sua file SQL da commit** — tao migration file moi `NNN_description.sql` trong `database/migrations/`.
2. Them/sua entity → viet Configuration file moi (1 file/entity), khong nhet vao DbContext.OnModelCreating.
3. `MedFlow.Application` khong bao gio `using Microsoft.EntityFrameworkCore` — chi goi `IApplicationDbContext`. Moi query EF phai nam trong Infrastructure.
4. Migration SQL phai co: `BEGIN;` ... `COMMIT;`, IF NOT EXISTS guards, index + FK theo pattern hien co (`medflow` schema, snake_case).
5. Sau khi them migration: cap nhat `database/` docs + changelog. **Hard gate**: moi schema change can review truoc khi apply.
