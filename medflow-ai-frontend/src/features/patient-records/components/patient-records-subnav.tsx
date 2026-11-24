import { FileText, FlaskConical, Pill, Settings } from 'lucide-react';
import { Link, useLocation } from 'react-router-dom';

interface SubNavTab {
  label: string;
  to: string;
  icon: typeof FileText;
  isActive: (pathname: string) => boolean;
}

const SUB_NAV_TABS: readonly SubNavTab[] = [
  {
    label: 'Hồ sơ bệnh án EMR',
    to: '/emr',
    icon: FileText,
    isActive: (p) =>
      p === '/emr' ||
      p === '/patient/emr' ||
      ((p.startsWith('/emr/') || p.startsWith('/patient/emr/')) &&
        !p.includes('/prescriptions') &&
        !p.includes('/diagnostic-result')),
  },
  {
    label: 'Đơn thuốc của tôi',
    to: '/patient/prescriptions',
    icon: Pill,
    isActive: (p) => p.startsWith('/patient/prescriptions') || p.startsWith('/emr/prescriptions'),
  },
  {
    label: 'Kết quả cận lâm sàng',
    to: '/patient/emr/diagnostic-result',
    icon: FlaskConical,
    isActive: (p) => p.includes('/diagnostic-result'),
  },
  {
    label: 'Cài đặt tài khoản',
    to: '/settings',
    icon: Settings,
    isActive: (p) => p === '/settings' || p === '/patient/settings',
  },
];

export function PatientRecordsSubnav() {
  const { pathname } = useLocation();

  return (
    <nav
      aria-label="Điều hướng hồ sơ sức khỏe"
      className="mb-6 flex flex-wrap items-center gap-2 rounded-2xl border border-border bg-white p-1.5 shadow-xs print:hidden"
    >
      {SUB_NAV_TABS.map(({ label, to, icon: Icon, isActive }) => {
        const active = isActive(pathname);
        return (
          <Link
            key={to}
            to={to}
            aria-current={active ? 'page' : undefined}
            className={`inline-flex items-center gap-2 rounded-xl px-3.5 py-2 text-xs font-semibold transition-colors focus-visible:ring-2 focus-visible:ring-primary ${
              active
                ? 'bg-primary text-white shadow-xs'
                : 'text-sub hover:bg-canvas hover:text-navy'
            }`}
          >
            <Icon className="h-4 w-4 shrink-0" aria-hidden="true" />
            <span>{label}</span>
          </Link>
        );
      })}
    </nav>
  );
}
