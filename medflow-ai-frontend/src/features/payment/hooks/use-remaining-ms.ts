import { useEffect, useState } from 'react';

/** Đếm ngược tới `expiresAt` (epoch ms). Trả 0 khi chưa có mốc hoặc đã hết. */
export function useRemainingMs(expiresAt: number | null): number {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    if (expiresAt === null) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [expiresAt]);

  if (expiresAt === null) return 0;
  return Math.max(0, expiresAt - now);
}
