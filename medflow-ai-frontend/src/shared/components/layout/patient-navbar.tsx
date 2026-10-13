import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Bell, Menu, Phone, X } from 'lucide-react';
import { MedflowLogo } from './medflow-logo';
import { UserMenu, type UserMenuUser } from './user-menu';

const NAV_LINKS = [
  { label: 'Trang chủ', to: '/', active: true },
  { label: 'Đặt lịch khám', to: '/#booking' },
  { label: 'Bác sĩ', to: '/#doctors' },
  { label: 'Lịch hẹn của tôi', to: '/appointments' },
  { label: 'Hồ sơ bệnh án', to: '/emr' },
];

interface PatientNavbarProps {
  user?: UserMenuUser | null;
  notificationCount?: number;
  onLogout?: () => void;
}

/** Header bệnh nhân đã đăng nhập theo Figma `10:106`. */
export function PatientNavbar({ user, notificationCount = 0, onLogout }: PatientNavbarProps) {
  const [open, setOpen] = useState(false);

  return (
    <header className="sticky top-0 z-40 w-full border-b border-border bg-white">
      <div className="mx-auto flex h-[72px] max-w-7xl items-center justify-between px-6">
        <MedflowLogo variant="full" className="h-11 w-auto" />

        <nav className="hidden items-center gap-6 lg:flex" aria-label="Điều hướng chính">
          {NAV_LINKS.map((link) => (
            <Link
              key={link.label}
              to={link.to}
              className={`border-b-2 pb-0.5 text-sm ${
                link.active
                  ? 'border-primary font-semibold text-primary'
                  : 'border-transparent text-sub hover:text-navy'
              }`}
            >
              {link.label}
            </Link>
          ))}
        </nav>

        <div className="hidden items-center gap-3 lg:flex">
          <a
            href="tel:19006868"
            className="flex items-center gap-1.5 text-xs font-semibold text-sub hover:text-navy"
          >
            <Phone className="h-3.5 w-3.5" />
            1900 6868
          </a>

          <button
            type="button"
            className="relative rounded-lg border border-border bg-canvas p-2 text-sub transition-colors hover:bg-slate-100"
            aria-label="Thông báo"
          >
            <Bell className="h-4 w-4" />
            {notificationCount > 0 && (
              <span className="absolute -right-1.5 -top-1.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-danger px-1 text-[10px] font-bold text-white">
                {notificationCount}
              </span>
            )}
          </button>

          <UserMenu user={user ?? null} onLogout={onLogout} />
        </div>

        <button
          type="button"
          className="rounded-lg p-2 text-slate-600 hover:bg-slate-100 lg:hidden"
          onClick={() => setOpen((o) => !o)}
          aria-label="Mở menu"
          aria-expanded={open}
        >
          {open ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
        </button>
      </div>

      {open && (
        <nav
          className="border-t border-border bg-white px-6 py-4 lg:hidden"
          aria-label="Menu di động"
        >
          <ul className="flex flex-col gap-3 text-sm">
            {NAV_LINKS.map((link) => (
              <li key={link.label}>
                <Link
                  to={link.to}
                  onClick={() => setOpen(false)}
                  className={link.active ? 'font-semibold text-primary' : 'text-sub'}
                >
                  {link.label}
                </Link>
              </li>
            ))}
            {onLogout && (
              <li className="border-t border-border pt-3">
                <button type="button" onClick={onLogout} className="text-danger">
                  Đăng xuất
                </button>
              </li>
            )}
          </ul>
        </nav>
      )}
    </header>
  );
}
