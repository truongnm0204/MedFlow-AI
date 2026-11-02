const ROLE_LANDING_PRIORITY: readonly { role: string; route: string; label: string }[] = [
  { role: 'SYSTEM_ADMIN', route: '/system-admin', label: 'Quản trị hệ thống' },
  { role: 'HOSPITAL_ADMIN', route: '/hospital-admin', label: 'Quản trị bệnh viện' },
  { role: 'DOCTOR', route: '/doctor', label: 'Bác sĩ lâm sàng' },
  { role: 'RECEPTION', route: '/reception', label: 'Nhân viên tiếp đón' },
  { role: 'DIAGNOSTIC_STAFF', route: '/diagnostic', label: 'KTV Cận lâm sàng' },
  { role: 'PHARMACY_STAFF', route: '/pharmacy', label: 'Dược sĩ bệnh viện' },
];

/** Trả về route dashboard mặc định theo danh sách role của user. */
export function getRoleLandingRoute(roles?: readonly string[] | null): string {
  if (!roles || roles.length === 0) return '/';
  for (const item of ROLE_LANDING_PRIORITY) {
    if (roles.includes(item.role)) return item.route;
  }
  return '/';
}

/** Trả về nhãn hiển thị tiếng Việt của role chính. */
export function getRoleDisplayLabel(roles?: readonly string[] | null): string {
  if (!roles || roles.length === 0) return 'Tài khoản bệnh nhân';
  for (const item of ROLE_LANDING_PRIORITY) {
    if (roles.includes(item.role)) return item.label;
  }
  return 'Tài khoản bệnh nhân';
}

/** Kiểm tra user có thuộc nhóm nhân viên bệnh viện / quản trị hay không. */
export function isStaffRole(roles?: readonly string[] | null): boolean {
  if (!roles || roles.length === 0) return false;
  return ROLE_LANDING_PRIORITY.some((item) => roles.includes(item.role));
}

/**
 * Xác định đích điều hướng sau đăng nhập:
 * Ưu tiên `returnUrl` nội bộ hợp lệ (bắt đầu bằng `/`, không phải `//`, `/login`, `/register`, `/verify-otp`),
 * ngược lại điều hướng về dashboard theo role.
 */
export function resolvePostLoginRedirect(
  returnUrl: string | null | undefined,
  roles?: readonly string[] | null,
): string {
  if (
    returnUrl &&
    returnUrl.startsWith('/') &&
    !returnUrl.startsWith('//') &&
    !returnUrl.startsWith('/login') &&
    !returnUrl.startsWith('/register') &&
    !returnUrl.startsWith('/verify-otp')
  ) {
    return returnUrl;
  }
  return getRoleLandingRoute(roles);
}
