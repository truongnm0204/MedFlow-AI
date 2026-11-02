import type { ReactNode } from 'react';
import { Navigate, useLocation, useSearchParams } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { resolvePostLoginRedirect } from '@/features/auth/utils/role-landing';

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
  const user = useAuthStore((s) => s.user);
  const [params] = useSearchParams();
  if (isAuthenticated) {
    return <Navigate to={resolvePostLoginRedirect(params.get('returnUrl'), user?.roles)} replace />;
  }
  return children;
}

/** Client-side navigation guard for admin screens; API authorization remains server-side. */
export function RequireAdmin({ children }: { children: ReactNode }) {
  const roles = useAuthStore((state) => state.user?.roles ?? []);
  if (!roles.some((role) => role === 'HOSPITAL_ADMIN' || role === 'SYSTEM_ADMIN')) {
    return <Navigate to="/" replace />;
  }
  return children;
}
