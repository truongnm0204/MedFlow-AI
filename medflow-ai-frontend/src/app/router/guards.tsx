import type { ReactNode } from 'react';
import { Navigate, useLocation, useSearchParams } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { hasAnyRole } from '@/features/auth/utils/rbac';
import { getRoleLandingRoute, resolvePostLoginRedirect } from '@/features/auth/utils/role-landing';
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

export interface RequireRoleProps {
  allowedRoles: readonly string[];
  children: ReactNode;
  fallbackTo?: string;
}

/** Generic client-side role guard; redirects unauthorized roles to their landing route. */
export function RequireRole({ allowedRoles, children, fallbackTo }: RequireRoleProps) {
  const roles = useAuthStore((state) => state.user?.roles ?? []);
  if (!hasAnyRole(roles, allowedRoles)) {
    return <Navigate to={fallbackTo ?? getRoleLandingRoute(roles)} replace />;
  }
  return children;
}

/** Client-side navigation guard for admin screens; API authorization remains server-side. */
export function RequireAdmin({ children }: { children: ReactNode }) {
  return <RequireRole allowedRoles={['HOSPITAL_ADMIN', 'SYSTEM_ADMIN']}>{children}</RequireRole>;
}

/** Client-side navigation guard for Hospital Admin-only screens; API authorization remains server-side. */
export function RequireHospitalAdmin({ children }: { children: ReactNode }) {
  return <RequireRole allowedRoles={['HOSPITAL_ADMIN']}>{children}</RequireRole>;
}

/** Client-side navigation guard for System Admin screens; API authorization remains server-side. */
export function RequireSystemAdmin({ children }: { children: ReactNode }) {
  return <RequireRole allowedRoles={['SYSTEM_ADMIN']}>{children}</RequireRole>;
}

/** Client-side navigation guard for Doctor workbench; API authorization remains server-side. */
export function RequireDoctor({ children }: { children: ReactNode }) {
  return <RequireRole allowedRoles={['DOCTOR']}>{children}</RequireRole>;
}

/** Client-side navigation guard for Reception workbench; API authorization remains server-side. */
export function RequireReception({ children }: { children: ReactNode }) {
  return <RequireRole allowedRoles={['RECEPTION']}>{children}</RequireRole>;
}

/** Client-side navigation guard for Diagnostic workbench; API authorization remains server-side. */
export function RequireDiagnostic({ children }: { children: ReactNode }) {
  return <RequireRole allowedRoles={['DIAGNOSTIC_STAFF']}>{children}</RequireRole>;
}

/** Client-side navigation guard for Pharmacy workbench; API authorization remains server-side. */
export function RequirePharmacy({ children }: { children: ReactNode }) {
  return <RequireRole allowedRoles={['PHARMACY_STAFF']}>{children}</RequireRole>;
}
