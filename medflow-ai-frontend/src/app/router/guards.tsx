import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';

/** Chặn route cần đăng nhập — chưa login → /login?returnUrl=<current>. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const location = useLocation();

  if (!isAuthenticated) {
    const returnUrl = encodeURIComponent(location.pathname + location.search);
    return <Navigate to={`/login?returnUrl=${returnUrl}`} replace />;
  }
  return children;
}

/** Chặn route chỉ dành cho khách — đã login → về '/'. */
export function GuestOnly({ children }: { children: ReactNode }) {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  if (isAuthenticated) return <Navigate to="/" replace />;
  return children;
}
