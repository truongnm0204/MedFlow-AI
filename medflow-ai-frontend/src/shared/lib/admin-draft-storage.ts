export type AdminDraftFormType =
  | 'department-create'
  | 'room-create'
  | 'doctor-create'
  | 'examination-service-create'
  | 'examination-slot-create';

export interface AdminDraftScopeInput {
  userId?: number | string | null;
  hospitalScope?: string | null;
  legacyUnauthenticatedKey?: string;
}

export type SaveAdminDraftResult = { ok: true; key: string } | { ok: false; error: string };

export const ADMIN_DRAFT_STORAGE_PREFIX = 'medflow.draft:v1:';

const LEGACY_DRAFT_KEYS = [
  'medflow.admin-doctor-create.draft',
  'medflow.room-create.draft',
] as const;

const FORBIDDEN_DRAFT_FIELDS = new Set([
  'password',
  'confirmPassword',
  'accessToken',
  'refreshToken',
  'token',
  'portrait',
]);

const cleanupListeners = new Set<() => void>();

function normalizeScopeToken(raw: string | number | null | undefined, fallback: string): string {
  if (raw === null || raw === undefined) return fallback;
  const cleaned = String(raw)
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9_-]+/g, '-')
    .replace(/^-+|-+$/g, '');
  return cleaned || fallback;
}

export function buildAdminDraftStorageKey(
  formType: AdminDraftFormType,
  scope: AdminDraftScopeInput = {},
): string {
  const hasAuthenticatedUser =
    scope.userId !== null && scope.userId !== undefined && String(scope.userId).trim() !== '';

  if (!hasAuthenticatedUser && scope.legacyUnauthenticatedKey) {
    return scope.legacyUnauthenticatedKey;
  }

  const userToken = normalizeScopeToken(scope.userId, 'anon');
  const hospitalToken = normalizeScopeToken(scope.hospitalScope, 'default');
  return `${ADMIN_DRAFT_STORAGE_PREFIX}u=${userToken}:h=${hospitalToken}:form=${formType}`;
}

function sanitizeDraftObject<T>(value: T): T {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return value;
  }
  const copy: Record<string, unknown> = {};
  for (const [key, entry] of Object.entries(value as Record<string, unknown>)) {
    if (FORBIDDEN_DRAFT_FIELDS.has(key)) {
      continue;
    }
    copy[key] = entry;
  }
  return copy as T;
}

export function saveAdminDraft<T extends object>(
  formType: AdminDraftFormType,
  payload: T,
  scope: AdminDraftScopeInput = {},
): SaveAdminDraftResult {
  if (typeof window === 'undefined' || typeof window.sessionStorage === 'undefined') {
    return { ok: false, error: 'Bộ nhớ phiên trình duyệt hiện không khả dụng.' };
  }

  const key = buildAdminDraftStorageKey(formType, scope);
  try {
    const sanitized = sanitizeDraftObject(payload);
    window.sessionStorage.setItem(key, JSON.stringify(sanitized));
    return { ok: true, key };
  } catch {
    return {
      ok: false,
      error:
        'Không thể lưu bản nháp vào bộ nhớ trình duyệt. Dữ liệu bạn đang nhập vẫn được giữ trên biểu mẫu.',
    };
  }
}

export function loadAdminDraft<T>(
  formType: AdminDraftFormType,
  scope: AdminDraftScopeInput = {},
): T | null {
  if (typeof window === 'undefined' || typeof window.sessionStorage === 'undefined') {
    return null;
  }

  const key = buildAdminDraftStorageKey(formType, scope);
  try {
    const raw = window.sessionStorage.getItem(key);
    if (!raw) return null;
    return JSON.parse(raw) as T;
  } catch {
    return null;
  }
}

export function removeAdminDraft(
  formType: AdminDraftFormType,
  scope: AdminDraftScopeInput = {},
): void {
  if (typeof window === 'undefined' || typeof window.sessionStorage === 'undefined') {
    return;
  }

  const key = buildAdminDraftStorageKey(formType, scope);
  try {
    window.sessionStorage.removeItem(key);
  } catch {
    // Ignore storage removal errors in restricted environments.
  }
}

export function registerAdminDraftCleanup(listener: () => void): () => void {
  cleanupListeners.add(listener);
  return () => {
    cleanupListeners.delete(listener);
  };
}

export function clearAllAdminDrafts(): void {
  if (typeof window !== 'undefined' && typeof window.sessionStorage !== 'undefined') {
    try {
      const keysToRemove: string[] = [];
      for (let i = 0; i < window.sessionStorage.length; i += 1) {
        const key = window.sessionStorage.key(i);
        if (key && key.startsWith(ADMIN_DRAFT_STORAGE_PREFIX)) {
          keysToRemove.push(key);
        }
      }
      for (const legacyKey of LEGACY_DRAFT_KEYS) {
        keysToRemove.push(legacyKey);
      }
      for (const key of keysToRemove) {
        window.sessionStorage.removeItem(key);
      }
    } catch {
      // Ignore storage access errors during teardown/logout.
    }
  }

  for (const listener of cleanupListeners) {
    try {
      listener();
    } catch {
      // Ensure all cleanup listeners run.
    }
  }
}
