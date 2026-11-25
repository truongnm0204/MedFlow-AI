import type { ReactNode } from 'react';

import { useAuthStore } from '../store/auth.store';
import {
  canAccessRoute,
  hasAnyPermission,
  hasAnyRole,
  hasPermission,
  hasRole,
  normalizeRoleCodes,
  resolveEffectivePermissions,
} from '../utils/rbac';
import { getRoleDisplayLabel, getRoleLandingRoute, isStaffRole } from '../utils/role-landing';

export function useRoleAccess(explicitPermissions?: readonly string[] | null) {
  const user = useAuthStore((state) => state.user);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const roles = normalizeRoleCodes(user?.roles);
  const effectiveExplicit = explicitPermissions ?? user?.permissions;
  const permissions = resolveEffectivePermissions(roles, effectiveExplicit);

  return {
    user,
    roles,
    permissions,
    isAuthenticated,
    landingRoute: getRoleLandingRoute(roles),
    roleLabel: getRoleDisplayLabel(roles),
    isStaff: isStaffRole(roles),
    hasRole: (targetRole: string) => hasRole(roles, targetRole),
    hasAnyRole: (allowedRoles: readonly string[]) => hasAnyRole(roles, allowedRoles),
    hasPermission: (permissionCode: string) =>
      hasPermission(roles, permissionCode, effectiveExplicit),
    hasAnyPermission: (permissionCodes: readonly string[]) =>
      hasAnyPermission(roles, permissionCodes, effectiveExplicit),
    canAccessRoute: (pathname: string) => canAccessRoute(pathname, roles, effectiveExplicit),
  };
}

export interface CanAccessProps {
  allowedRoles?: readonly string[];
  requiredPermissions?: readonly string[];
  explicitPermissions?: readonly string[] | null;
  fallback?: ReactNode;
  children: ReactNode;
}

export function CanAccess({
  allowedRoles,
  requiredPermissions,
  explicitPermissions,
  fallback = null,
  children,
}: CanAccessProps) {
  const { hasAnyRole: checkAnyRole, hasAnyPermission: checkAnyPermission } =
    useRoleAccess(explicitPermissions);

  if (allowedRoles && allowedRoles.length > 0 && !checkAnyRole(allowedRoles)) {
    return fallback;
  }

  if (
    requiredPermissions &&
    requiredPermissions.length > 0 &&
    !checkAnyPermission(requiredPermissions)
  ) {
    return fallback;
  }

  return children;
}
