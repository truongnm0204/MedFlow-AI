# MCP Artifact Hygiene

Moi file do MCP/AI tool (Figma, Playwright, Stitch, ai-multimodal, repomix...) tao ra deu la **ephemeral** theo mac dinh. Khong bao gio de artifact lo lung o repo root, `src/`, hoac commit neu khong co quyet dinh durable.

## Phan loai

| Loai | Dinh nghia | Vi tri |
|---|---|---|
| Ephemeral | File chi phuc vu 1 lan verify/debug, xong bo | `.mcp-artifacts/<tool>/` — **gitignored** |
| Durable | Asset gan voi docs/plan dai han | `docs/assets/` hoac `plans/<plan-dir>/assets/` — **commit** |

## Quy tac

1. **Truoc khi goi MCP tao file**: chi dinh output path vao `.mcp-artifacts/<tool>/`. Khong de tool tu chon repo root.
2. **Sau khi dung xong**: phan loai ngay — keep (move sang durable) hoac delete. Khong de "de do da".
3. **Dat ten**: `{tool}-{yyyymmdd-hhmm}-{slug}.{ext}` — vi du `figma-20261013-0145-login-screen.png`.
4. **Figma cu the**: `generate_image` URL het han sau 7 ngay, `export_video` TTL 30s-7d. Neu can durable → download ngay trong cung session vao `docs/assets/` hoac `plans/<plan>/assets/`, kem ghi chu nguon (node id, file key).
5. **Pre-commit**: review `git status --short` — file artifact lot vao staging = vi pham.
6. **Dinh ky**: `.mcp-artifacts/` duoc phep xoa sach bat cu luc nao — khong chua gi durable. Sweep cuoi moi sprint/milestone.

## Hien trang da biet

- `.playwright-mcp/` o root = ephemeral, da gitignore.
- `plans/visuals/` DA move → `plans/261012-2037-authen-and-homepage/assets/` (durable reference). Thu muc `visuals/` khong con — dung `assets/` tu nay.
