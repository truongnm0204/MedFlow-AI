import { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { CalendarDays, Menu, X } from 'lucide-react';
import { MedflowLogo } from './medflow-logo';
import { BOOKING_ACTIVE_PATTERNS, PUBLIC_NAV_LINKS, isNavLinkActive } from './nav-links';
import { Button } from '../../ui/button';

const BOOKING_CTA_CLASSES =
  'inline-flex items-center justify-center gap-1.5 rounded-lg bg-primary px-3 py-1.5 text-xs font-semibold text-white shadow-sm transition-colors hover:bg-primary-dark focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2';
const BOOKING_CTA_ACTIVE = 'ring-2 ring-sky-200 ring-offset-2';

/** Navbar trạng thái khách theo Figma `10:183`. */
export function PublicNavbar() {
  const [open, setOpen] = useState(false);
  const { pathname } = useLocation();
  const bookingActive = BOOKING_ACTIVE_PATTERNS.some((pattern) => pathname === pattern);

  return (
    <header className="sticky top-0 z-40 w-full border-b border-border bg-white">
      <div className="mx-auto flex h-16 max-w-7xl items-center justify-between px-6">
        <div className="flex items-center gap-2">
          <MedflowLogo variant="icon" className="h-10 w-10" />
          <div className="leading-tight">
            <Link to="/" className="text-base font-semibold text-navy">
              MedFlow<span className="text-primary">AI</span>
            </Link>
            <p className="text-[10px] font-bold uppercase tracking-wider text-sub">
              Hospital Portal
            </p>
          </div>
        </div>

        <nav className="hidden items-center gap-7 lg:flex" aria-label="Điều hướng chính">
          {PUBLIC_NAV_LINKS.map((link) => {
            const active = isNavLinkActive(pathname, link);
            return (
              <Link
                key={link.label}
                to={link.to}
                aria-current={active ? 'page' : undefined}
                className={`border-b-2 pb-0.5 text-sm ${
                  active
                    ? 'border-primary font-semibold text-primary'
                    : 'border-transparent font-normal text-sub hover:text-navy'
                }`}
              >
                {link.label}
              </Link>
            );
          })}
        </nav>

        <div className="hidden items-center gap-3 lg:flex">
          <Button variant="ghost" size="sm">
            <Link to="/login">Đăng nhập</Link>
            <span className="text-slate-300">/</span>
            <Link to="/register">Đăng ký</Link>
          </Button>
          <Link
            to="/booking"
            aria-current={bookingActive ? 'page' : undefined}
            className={`${BOOKING_CTA_CLASSES} ${bookingActive ? BOOKING_CTA_ACTIVE : ''}`}
          >
            <CalendarDays className="h-4 w-4" aria-hidden="true" />
            Đặt lịch khám
          </Link>
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
            {PUBLIC_NAV_LINKS.map((link) => {
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
            <li className="flex gap-3 pt-2">
              <Button variant="secondary" size="sm" className="flex-1">
                <Link to="/login">Đăng nhập / Đăng ký</Link>
              </Button>
              <Link
                to="/booking"
                onClick={() => setOpen(false)}
                aria-current={bookingActive ? 'page' : undefined}
                className={`${BOOKING_CTA_CLASSES} flex-1 ${bookingActive ? BOOKING_CTA_ACTIVE : ''}`}
              >
                <CalendarDays className="h-4 w-4" aria-hidden="true" />
                Đặt lịch khám
              </Link>
            </li>
          </ul>
        </nav>
      )}
    </header>
  );
}
