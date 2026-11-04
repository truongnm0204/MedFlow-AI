export const SYSTEM_ROLE_CODES = [
  'SYSTEM_ADMIN',
  'HOSPITAL_ADMIN',
  'DOCTOR',
  'RECEPTION',
  'DIAGNOSTIC_STAFF',
  'PHARMACY_STAFF',
  'PATIENT',
] as const;

export type SystemRoleCode = (typeof SYSTEM_ROLE_CODES)[number];

export const SYSTEM_PERMISSION_CODES = [
  'PATIENT_PROFILE_READ',
  'APPOINTMENT_BOOK',
  'PATIENT_CHECKIN',
  'VISIT_ASSIGN',
  'QUEUE_MANAGE',
  'MEDICAL_RECORD_WRITE',
  'DIAGNOSTIC_ORDER',
  'DIAGNOSTIC_RESULT_WRITE',
  'PRESCRIPTION_ISSUE',
  'MEDICINE_DISPENSE',
  'HOSPITAL_CONFIG',
  'USER_MANAGE',
  'AUDIT_READ',
  'PAYMENT_VIEW',
] as const;

export type SystemPermissionCode = (typeof SYSTEM_PERMISSION_CODES)[number];

export const ROLE_DEFAULT_PERMISSIONS: Readonly<Record<string, readonly SystemPermissionCode[]>> = {
  SYSTEM_ADMIN: ['HOSPITAL_CONFIG', 'USER_MANAGE', 'AUDIT_READ'],
  HOSPITAL_ADMIN: ['VISIT_ASSIGN', 'QUEUE_MANAGE', 'HOSPITAL_CONFIG', 'PAYMENT_VIEW'],
  DOCTOR: [
    'PATIENT_PROFILE_READ',
    'MEDICAL_RECORD_WRITE',
    'DIAGNOSTIC_ORDER',
    'PRESCRIPTION_ISSUE',
    'PAYMENT_VIEW',
  ],
  RECEPTION: [
    'PATIENT_PROFILE_READ',
    'APPOINTMENT_BOOK',
    'PATIENT_CHECKIN',
    'VISIT_ASSIGN',
    'QUEUE_MANAGE',
    'PAYMENT_VIEW',
  ],
  DIAGNOSTIC_STAFF: ['PATIENT_PROFILE_READ', 'QUEUE_MANAGE', 'DIAGNOSTIC_RESULT_WRITE'],
  PHARMACY_STAFF: ['PATIENT_PROFILE_READ', 'MEDICINE_DISPENSE', 'PAYMENT_VIEW'],
  PATIENT: ['PATIENT_PROFILE_READ', 'APPOINTMENT_BOOK', 'PAYMENT_VIEW'],
  HEAD_DOCTOR: [
    'PATIENT_PROFILE_READ',
    'VISIT_ASSIGN',
    'QUEUE_MANAGE',
    'MEDICAL_RECORD_WRITE',
    'DIAGNOSTIC_ORDER',
    'PRESCRIPTION_ISSUE',
  ],
  CLINICAL_NURSE: [
    'PATIENT_PROFILE_READ',
    'PATIENT_CHECKIN',
    'QUEUE_MANAGE',
    'MEDICAL_RECORD_WRITE',
  ],
  SECURITY_AUDITOR: ['AUDIT_READ'],
};

export interface RouteRoleAccessRule {
  prefix: string;
  label: string;
  guardName: string;
  allowedRoles: readonly SystemRoleCode[];
  requiredPermission?: SystemPermissionCode;
}

export const ROUTE_ROLE_ACCESS_RULES: readonly RouteRoleAccessRule[] = [
  {
    prefix: '/system-admin',
    label: 'Tổng quan Quản trị Hệ thống',
    guardName: 'RequireSystemAdmin',
    allowedRoles: ['SYSTEM_ADMIN'],
    requiredPermission: 'USER_MANAGE',
  },
  {
    prefix: '/admin/staff/new',
    label: 'Tạo tài khoản nhân sự',
    guardName: 'RequireSystemAdmin',
    allowedRoles: ['SYSTEM_ADMIN'],
    requiredPermission: 'USER_MANAGE',
  },
  {
    prefix: '/admin/staff',
    label: 'Danh sách tài khoản nhân sự',
    guardName: 'RequireAdmin',
    allowedRoles: ['HOSPITAL_ADMIN', 'SYSTEM_ADMIN'],
    requiredPermission: 'USER_MANAGE',
  },
  {
    prefix: '/admin/roles',
    label: 'Danh mục Vai trò & Phân quyền (UC58)',
    guardName: 'RequireAdmin',
    allowedRoles: ['HOSPITAL_ADMIN', 'SYSTEM_ADMIN'],
    requiredPermission: 'USER_MANAGE',
  },
  {
    prefix: '/hospital-admin',
    label: 'Tổng quan Quản trị Cơ sở',
    guardName: 'RequireAdmin',
    allowedRoles: ['HOSPITAL_ADMIN', 'SYSTEM_ADMIN'],
    requiredPermission: 'HOSPITAL_CONFIG',
  },
  {
    prefix: '/admin',
    label: 'Cổng Quản trị Cơ sở & Danh mục',
    guardName: 'RequireAdmin',
    allowedRoles: ['HOSPITAL_ADMIN', 'SYSTEM_ADMIN'],
    requiredPermission: 'HOSPITAL_CONFIG',
  },
  {
    prefix: '/doctor',
    label: 'Bàn khám Bác sĩ lâm sàng',
    guardName: 'RequireDoctor',
    allowedRoles: ['DOCTOR'],
    requiredPermission: 'MEDICAL_RECORD_WRITE',
  },
  {
    prefix: '/reception',
    label: 'Bàn lễ tân Tiếp đón',
    guardName: 'RequireReception',
    allowedRoles: ['RECEPTION'],
    requiredPermission: 'PATIENT_CHECKIN',
  },
  {
    prefix: '/diagnostic',
    label: 'Bàn Kỹ thuật Cận lâm sàng',
    guardName: 'RequireDiagnostic',
    allowedRoles: ['DIAGNOSTIC_STAFF'],
    requiredPermission: 'DIAGNOSTIC_RESULT_WRITE',
  },
  {
    prefix: '/pharmacy',
    label: 'Bàn Dược sĩ Nhà thuốc',
    guardName: 'RequirePharmacy',
    allowedRoles: ['PHARMACY_STAFF'],
    requiredPermission: 'MEDICINE_DISPENSE',
  },
];

export function normalizeRoleCodes(roles?: readonly string[] | null): string[] {
  if (!roles || roles.length === 0) return [];
  const unique = new Set<string>();
  for (const role of roles) {
    if (typeof role === 'string') {
      const normalized = role.trim().toUpperCase();
      if (normalized) unique.add(normalized);
    }
  }
  return Array.from(unique);
}

export function hasRole(roles: readonly string[] | null | undefined, targetRole: string): boolean {
  const normalizedTarget = targetRole.trim().toUpperCase();
  if (!normalizedTarget) return false;
  return normalizeRoleCodes(roles).includes(normalizedTarget);
}

export function hasAnyRole(
  roles: readonly string[] | null | undefined,
  allowedRoles: readonly string[],
): boolean {
  if (allowedRoles.length === 0) return false;
  const userRoles = normalizeRoleCodes(roles);
  if (userRoles.length === 0) return false;
  const normalizedAllowed = new Set(allowedRoles.map((role) => role.trim().toUpperCase()));
  return userRoles.some((role) => normalizedAllowed.has(role));
}

export function resolveEffectivePermissions(
  roles?: readonly string[] | null,
  explicitPermissions?: readonly string[] | null,
): string[] {
  if (explicitPermissions && explicitPermissions.length > 0) {
    const unique = new Set<string>();
    for (const code of explicitPermissions) {
      if (typeof code === 'string') {
        const normalized = code.trim().toUpperCase();
        if (normalized) unique.add(normalized);
      }
    }
    return Array.from(unique);
  }

  const userRoles = normalizeRoleCodes(roles);
  const permissions = new Set<string>();
  for (const role of userRoles) {
    const defaults = ROLE_DEFAULT_PERMISSIONS[role];
    if (defaults) {
      for (const perm of defaults) {
        permissions.add(perm);
      }
    }
  }
  return Array.from(permissions);
}

export function hasPermission(
  roles: readonly string[] | null | undefined,
  permissionCode: string,
  explicitPermissions?: readonly string[] | null,
): boolean {
  const normalizedTarget = permissionCode.trim().toUpperCase();
  if (!normalizedTarget) return false;
  return resolveEffectivePermissions(roles, explicitPermissions).includes(normalizedTarget);
}

export function hasAnyPermission(
  roles: readonly string[] | null | undefined,
  permissionCodes: readonly string[],
  explicitPermissions?: readonly string[] | null,
): boolean {
  if (permissionCodes.length === 0) return false;
  const effective = new Set(resolveEffectivePermissions(roles, explicitPermissions));
  return permissionCodes.some((code) => effective.has(code.trim().toUpperCase()));
}

export function matchRouteAccessRule(pathname: string): RouteRoleAccessRule | null {
  const rawPath = pathname.split(/[?#]/)[0]?.trim() || '/';
  const cleanPath = rawPath.length > 1 && rawPath.endsWith('/') ? rawPath.slice(0, -1) : rawPath;

  if (/^\/admin\/staff\/[^/]+\/edit$/i.test(cleanPath)) {
    return {
      prefix: '/admin/staff/:id/edit',
      label: 'Cập nhật tài khoản nhân sự',
      guardName: 'RequireSystemAdmin',
      allowedRoles: ['SYSTEM_ADMIN'],
      requiredPermission: 'USER_MANAGE',
    };
  }

  for (const rule of ROUTE_ROLE_ACCESS_RULES) {
    if (cleanPath === rule.prefix || cleanPath.startsWith(`${rule.prefix}/`)) {
      return rule;
    }
  }
  return null;
}

export function getRequiredRolesForPath(pathname: string): readonly SystemRoleCode[] | null {
  const rule = matchRouteAccessRule(pathname);
  return rule ? rule.allowedRoles : null;
}

export function canAccessRoute(pathname: string, roles?: readonly string[] | null): boolean {
  const rule = matchRouteAccessRule(pathname);
  if (!rule) return true;
  return hasAnyRole(roles, rule.allowedRoles);
}
