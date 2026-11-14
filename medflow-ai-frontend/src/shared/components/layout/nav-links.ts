export interface NavLinkItem {
  label: string;
  to: string;
  /**
   * Pattern `'X'` = exact pathname match; `'X/*'` = `X` or any sub-path of `X`.
   * Hash links (`/#…`) use `[]` → never active.
   */
  activePatterns: string[];
}

export function isNavLinkActive(pathname: string, link: NavLinkItem): boolean {
  return link.activePatterns.some((pattern) => {
    if (pattern.endsWith('/*')) {
      const prefix = pattern.slice(0, -2);
      return pathname === prefix || pathname.startsWith(`${prefix}/`);
    }
    return pathname === pattern;
  });
}

/** Active patterns for the "Đặt lịch khám" CTA in PublicNavbar. */
export const BOOKING_ACTIVE_PATTERNS = ['/booking', '/appointment/book'];

export const PATIENT_NAV_LINKS: NavLinkItem[] = [
  { label: 'Trang chủ', to: '/', activePatterns: ['/', '/patient', '/patient/home'] },
  { label: 'Đặt lịch khám', to: '/booking', activePatterns: BOOKING_ACTIVE_PATTERNS },
  { label: 'Bác sĩ', to: '/doctors', activePatterns: ['/doctors/*', '/appointment/doctors/*'] },
  {
    label: 'Dịch vụ khám',
    to: '/services',
    activePatterns: ['/services/*', '/appointment/services/*'],
  },
  { label: 'Lịch hẹn của tôi', to: '/appointments', activePatterns: ['/appointments/*'] },
  { label: 'Viện phí', to: '/payments', activePatterns: ['/payments/*'] },
  { label: 'Hồ sơ bệnh án', to: '/emr', activePatterns: ['/emr/*'] },
];

export const PUBLIC_NAV_LINKS: NavLinkItem[] = [
  { label: 'Trang chủ', to: '/', activePatterns: ['/'] },
  { label: 'Bác sĩ', to: '/doctors', activePatterns: ['/doctors/*', '/appointment/doctors/*'] },
  {
    label: 'Dịch vụ khám',
    to: '/services',
    activePatterns: ['/services/*', '/appointment/services/*'],
  },
  { label: 'Chuyên khoa', to: '/#specialties', activePatterns: [] },
  { label: 'Giới thiệu', to: '/#about', activePatterns: [] },
];
