# Rule: Frontend UI/UX Consistency & Design System (`ui-ux-pro-max`)

Applies to all files under `medflow-ai-frontend/src/`. Every frontend feature, refactor, or review MUST enforce these rules alongside `docs/frontend-code-standards.md` and `skill://ui-ux-pro-max`.

## 1. Typography & Design Tokens (`src/index.css`)
- **Typography**: `Inter Variable` (`font-sans`) for all UI copy; `font-mono` exclusively for medical/operational codes (`PAT-*`, `DOC-*`, `CLS-*`, queue numbers, room codes, license numbers).
- **Color tokens**: Use semantic classes defined in `src/index.css` `@theme`:
  - `bg-canvas` (`#f8fafc`) instead of `bg-[#F8FAFC]`
  - `text-ink` (`#0f172a`) instead of `text-[#0F172A]`
  - `text-navy` (`#0a2540`) instead of `text-[#0A2540]`
  - `text-sub` (`#49607e`) for secondary navigation/metadata text
  - `border-border` (`#e2e8f0`) for standard container/table borders
  - `bg-primary` / `text-primary` (`#0284c7`) and `hover:bg-primary-dark` (`#0369a1`) for primary brand actions
- **Contrast & states**: Maintain >= 4.5:1 text contrast on light surfaces; provide visible `focus-visible:ring-2`, smooth transitions (`150-200ms`), and explicit `disabled:opacity-60` states without layout-shifting transforms.

## 2. Icon System & Accessibility
- **Single icon library**: Use `lucide-react` across all features with consistent stroke width (`strokeWidth={2}` or `1.75`) and Tailwind size tokens (`h-4 w-4`, `h-5 w-5`).
- **Forbidden**:
  - Emojis as structural/navigation/status icons.
  - Scattered `<img src="/assets/.../*.svg" />` files for UI icons/buttons.
- **Accessibility**:
  - Decorative icons beside visible text MUST include `aria-hidden="true"`.
  - Icon-only interactive buttons (`<button>`) MUST have an explicit `aria-label` (and `aria-expanded` / `aria-pressed` when stateful).

## 3. Canonical Layout Shells
- **Patient Portal Shell** (`home`, `patient`, `family`, `appointment`):
  - Root wrapper: `<div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">`
  - Shared `<AppHeader />` + `<main className="flex-1">` (`mx-auto max-w-7xl px-4 sm:px-6 lg:px-8`) + `<PatientFooter />`.
- **Clinical Workbench Shell** (`reception`, `doctor`, `diagnostic`, `pharmacy`):
  - Root wrapper: `<div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">`
  - Left collapsible Sidebar (`w-[260px]` expanded / `w-[68px]` collapsed) using `<MedflowLogo />`.
  - Top bar `<header className="flex h-16 items-center justify-between border-b border-border bg-white px-6">`.
  - Main content (`p-6 space-y-6`): 4 KPI Stat Cards (`@/shared/ui/card`) + 12-column grid (`lg:col-span-8` Queue/Worklist Table + `lg:col-span-4` Quick Actions & Secondary Preview) + `@/shared/ui/badge`.

## 4. Feature Directory Hygiene & Formatting Gate
- **No duplicate feature folders**: Keep one canonical folder per domain in `src/features/`. Do not leave empty duplicate folders (`0 files`).
- **Formatting gate**: `npm run format:check` (Prettier) + `npm run lint` (oxlint) + `npx tsc -b` + `npm test -- --run` MUST all pass with 0 errors before commit/PR.
