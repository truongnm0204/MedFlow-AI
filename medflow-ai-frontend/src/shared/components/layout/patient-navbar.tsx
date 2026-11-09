import { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { Menu, Phone, X } from 'lucide-react';

import { NotificationBellPopover } from '@/features/appointment/components/notification-bell-popover';
import { MedflowLogo } from './medflow-logo';
import { PATIENT_NAV_LINKS, isNavLinkActive } from './nav-links';
import { UserMenu, type UserMenuUser } from './user-menu';

interface PatientNavbarProps {
  user?: UserMenuUser | null;
  notificationCount?: number;
  onLogout?: () => void;
}

/** Header bệnh nhân đã đăng nhập theo Figma `10:106`. */
export function PatientNavbar({
  user,
  notificationCount: _notificationCount = 0,
  onLogout,
}: PatientNavbarProps) {
  const [open, setOpen] = useState(false);
  const { pathname } = useLocation();

  return (
    <header className="sticky top-0 z-40 w-full border-b border-border bg-white">
      <div className="mx-auto flex h-[72px] max-w-7xl items-center justify-between px-6">
        <MedflowLogo variant="full" className="h-11 w-auto" />

        <nav className="hidden items-center gap-6 xl:flex" aria-label="Điều hướng chính">
          {PATIENT_NAV_LINKS.map((link) => {
            const active = isNavLinkActive(pathname, link);
            return (
              <Link
                key={link.label}
                to={link.to}
                aria-current={active ? 'page' : undefined}
                className={`border-b-2 pb-0.5 text-sm ${
                  active
                    ? 'border-primary font-semibold text-primary'
                    : 'border-transparent text-sub hover:text-navy'
                }`}
              >
                {link.label}
              </Link>
            );
          })}
        </nav>

        <div className="hidden items-center gap-3 xl:flex">
          <a
            href="tel:19006868"
            className="flex items-center gap-1.5 text-xs font-semibold text-sub hover:text-navy"
          >
            <Phone className="h-3.5 w-3.5" aria-hidden="true" />
            1900 6868
          </a>

          <NotificationBellPopover />

          <UserMenu user={user ?? null} onLogout={onLogout} />
        </div>

        <button
          type="button"
          className="rounded-lg p-2 text-slate-600 hover:bg-slate-100 xl:hidden"
          onClick={() => setOpen((o) => !o)}
          aria-label="Mở menu"
          aria-expanded={open}
        >
          {open ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
        </button>
      </div>

      {open && (
        <nav
          className="border-t border-border bg-white px-6 py-4 xl:hidden"
          aria-label="Menu di động"
        >
          <ul className="flex flex-col gap-3 text-sm">
            {PATIENT_NAV_LINKS.map((link) => {
              const active = isNavLinkActive(pathname, link);
              return (
                <li key={link.label}>
                  <Link
                    to={link.to}
                    onClick={() => setOpen(false)}
                    aria-current={active ? 'page' : undefined}
                    className={active ? 'font-semibold text-primary' : 'text-sub'}
                  >
                    {link.label}
                  </Link>
                </li>
              );
            })}
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
