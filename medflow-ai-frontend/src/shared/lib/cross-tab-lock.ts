const LEASE_TTL_MS = 10_000;
const WAIT_TIMEOUT_MS = 12_000;
const VERIFY_DELAY_MS = 15;

interface StorageLease {
  owner: string;
  expiresAt: number;
}

function createOwnerId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => {
    setTimeout(resolve, ms);
  });
}

function readLease(leaseKey: string): StorageLease | null {
  try {
    const raw = localStorage.getItem(leaseKey);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<StorageLease>;
    if (typeof parsed.owner === 'string' && typeof parsed.expiresAt === 'number') {
      return { owner: parsed.owner, expiresAt: parsed.expiresAt };
    }
    return null;
  } catch {
    return null;
  }
}

async function withLocalStorageLease<T>(lockName: string, task: () => Promise<T>): Promise<T> {
  const leaseKey = `medflow.lock.${lockName}`;
  const owner = createOwnerId();
  const deadline = Date.now() + WAIT_TIMEOUT_MS;

  while (Date.now() < deadline) {
    const existing = readLease(leaseKey);
    if (!existing || existing.expiresAt <= Date.now()) {
      const candidate: StorageLease = {
        owner,
        expiresAt: Date.now() + LEASE_TTL_MS,
      };
      localStorage.setItem(leaseKey, JSON.stringify(candidate));
      await sleep(VERIFY_DELAY_MS);

      const confirmed = readLease(leaseKey);
      if (confirmed?.owner === owner) {
        try {
          return await task();
        } finally {
          const current = readLease(leaseKey);
          if (current?.owner === owner) {
            localStorage.removeItem(leaseKey);
          }
        }
      }
    }

    await sleep(40 + Math.floor(Math.random() * 40));
  }

  throw new Error(`Timed out waiting for cross-tab lock: ${lockName}`);
}

/**
 * Chạy tác vụ độc quyền xuyên suốt các tab trình duyệt:
 * - Ưu tiên Web Locks API (`navigator.locks.request`).
 * - Fallback về lease lock có TTL trên `localStorage` khi môi trường không hỗ trợ Web Locks.
 */
export async function withCrossTabLock<T>(lockName: string, task: () => Promise<T>): Promise<T> {
  if (typeof navigator !== 'undefined' && typeof navigator.locks?.request === 'function') {
    return navigator.locks.request(lockName, { mode: 'exclusive' }, () => task());
  }

  if (typeof localStorage !== 'undefined') {
    return withLocalStorageLease(lockName, task);
  }

  return task();
}
