# Frontend Coding Standards

Applies to `medflow-ai-frontend/` — React 19, TypeScript, Vite, Tailwind CSS 4,
TanStack Query, Zustand, React Hook Form + Zod, Axios, Vitest.

## 1. Naming

| Thing | Convention | Example |
|---|---|---|
| Files/folders | kebab-case | `booking-summary.tsx`, `use-auth.ts` |
| Components | PascalCase | `BookingSummary` |
| Functions/variables | camelCase | `fetchPatient`, `isSubmitting` |
| Constants | UPPER_SNAKE | `MAX_RETRY_COUNT` |
| Types/interfaces | PascalCase | `AppointmentDto`, `BookingState` |
| Event handlers | `handle*` | `handleSubmit`, `handleRowClick` |
| Custom hooks | `use*` | `useAppointments` |
| Zustand stores | `use*Store` | `useBookingStore` |
| Zod schemas | `*Schema` | `loginSchema` |
| API files | `<resource>.api.ts` | `appointment.api.ts` |
| Tests | `<unit>.test.ts(x)` in `__tests__/` | `booking.store.test.ts` |

Boolean names read as predicates: `isLoading`, `hasError`, `canSubmit`,
`shouldRefresh` — never `loading`/`flag`/`check`.

## 2. Variables & TypeScript

- `const` by default; `let` only when reassignment is truly needed; **never `var`**.
- Prefer `interface` for object shapes, `type` for unions/generics/utilities.
- No `any`. Use `unknown` and narrow, or declare a proper type.
- Function parameters that are objects: destructure with defaults.
- Exported functions get explicit return types on public APIs; internal helpers
  may rely on inference.
- Derive DTO types from Zod schemas where possible:
  `type LoginInput = z.infer<typeof loginSchema>`.

```ts
// good
const [page, setPage] = useState(1)
const { data, isPending, error } = useAppointments({ page })

// avoid
let x: any = getData()
```

## 3. Imports

- Path alias `@/` maps to `src/` — always use it beyond one level up:
  `import { apiClient } from '@/shared/lib/axios'`.
- Type-only imports use `import type`:
  `import type { ReactNode } from 'react'`.
- Order: external packages → `@/` imports → relative imports. No unused imports
  (`noUnusedLocals` is on).

## 4. Feature folder contract

Each `src/features/<name>/` keeps the fixed layout already scaffolded:

```
api/         axios calls, typed in/out — no other files call axios directly
components/  presentational components (props in, events out)
hooks/       TanStack Query hooks + local hooks
pages/       route-level components (composition only)
schemas/     zod schemas (form validation + API payloads)
store/       zustand stores (client/UI state only)
types/       shared types for the feature
__tests__/   vitest tests mirroring source
```

Rules:
- Server data (fetch/cache) lives in **React Query hooks** — `useQuery` for reads,
  `useMutation` for writes. Never fetch in components or in `useEffect`.
- Zustand is for **client state only** (selections, wizard steps, UI flags).
- Cross-feature imports go through `src/shared/` — never import another
  feature's internals.

## 5. Templates

### Page / component

```tsx
interface LoginPageProps {
  redirectTo?: string
}

export function LoginPage({ redirectTo = '/' }: LoginPageProps) {
  const navigate = useNavigate()
  // ...
  return <main className="...">...</main>
}
```

- Named exports only (no `export default` except lazy routes).
- Props interface named `<Component>Props`; small props inlined are fine.
- Keep components under ~150 lines; extract sub-components early.

### API call

```ts
// features/auth/api/auth.api.ts
import { apiClient } from '@/shared/lib/axios'
import type { LoginRequest, LoginResponse } from '../types/auth.types'

export async function login(req: LoginRequest): Promise<LoginResponse> {
  const { data } = await apiClient.post<LoginResponse>('/auth/login', req)
  return data
}
```

### Query hook

```ts
// features/appointment/hooks/use-appointments.ts
export function useAppointments(filter: AppointmentFilter) {
  return useQuery({
    queryKey: ['appointments', filter],
    queryFn: () => fetchAppointments(filter),
  })
}

export function useBookAppointment() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: bookAppointment,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['appointments'] }),
  })
}
```

- Query keys: `['<resource>', ...params]` — consistent, serializable.

### Zustand store

```ts
interface BookingState {
  selectedDoctorId: string | null
  selectDoctor: (id: string) => void
  reset: () => void
}

export const useBookingStore = create<BookingState>((set) => ({ ... }))
```

### Form (RHF + Zod)

```ts
const schema = z.object({
  email: z.string().email(),
  password: z.string().min(8),
})
type FormValues = z.infer<typeof schema>

const form = useForm<FormValues>({ resolver: zodResolver(schema) })
```

### Test

```ts
// features/auth/__tests__/login.test.ts
describe('login', () => {
  it('stores the access token on success', async () => { ... })
})
```

## 6. Error handling

- All axios errors funnel through `apiClient` interceptors in
  `src/shared/lib/axios.ts` — extend there (401 refresh, error toast), don't
  scatter try/catch at call sites for cross-cutting concerns.
- In mutations use `onError` to surface user-facing messages.
- Never swallow errors silently; log or rethrow.

## 7. Style / UI

- Tailwind utility classes only; extract `ui/` primitives for repeated patterns.
- Responsive-first: design mobile layout, then `sm:`/`md:`/`lg:` up.
- All user-visible strings in Vietnamese (per product locale); keep medical
  terms accurate.

## 8. Quality gates

- `npm run lint` (oxlint) must pass before commit.
- `npm run test` (vitest) must pass before push.
- `tsc -b` clean — build runs strict-ish flags (`noUnusedLocals`,
  `noUnusedParameters`, `noFallthroughCasesInSwitch`, `verbatimModuleSyntax`).
- Files under ~200 lines; split when they grow.
