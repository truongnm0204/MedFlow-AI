import { useState } from 'react'
import { Link } from 'react-router-dom'
import { CalendarDays, Menu, X } from 'lucide-react'
import { MedflowLogo } from './medflow-logo'
import { Button } from '../../ui/button'

const NAV_LINKS = [
  { label: 'Trang chủ', href: '/', active: true },
  { label: 'Chuyên khoa', href: '/#specialties' },
  { label: 'Bác sĩ', href: '/#doctors' },
  { label: 'Dịch vụ khám', href: '/#services' },
  { label: 'Giới thiệu', href: '/#about' },
]

/** Navbar trạng thái khách theo Figma `10:183`. */
export function PublicNavbar() {
  const [open, setOpen] = useState(false)

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
          {NAV_LINKS.map((link) => (
            <Link
              key={link.label}
              to={link.href}
              className={`border-b-2 pb-0.5 text-sm ${
                link.active
                  ? 'border-primary font-semibold text-primary'
                  : 'border-transparent font-normal text-sub hover:text-navy'
              }`}
            >
              {link.label}
            </Link>
          ))}
        </nav>

        <div className="hidden items-center gap-3 lg:flex">
          <Button variant="ghost" size="sm">
            <Link to="/login">Đăng nhập</Link>
            <span className="text-slate-300">/</span>
            <Link to="/register">Đăng ký</Link>
          </Button>
          <Button size="sm">
            <CalendarDays className="h-4 w-4" />
            Đặt lịch khám
          </Button>
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
        <nav className="border-t border-border bg-white px-6 py-4 lg:hidden" aria-label="Menu di động">
          <ul className="flex flex-col gap-3 text-sm">
            {NAV_LINKS.map((link) => (
              <li key={link.label}>
                <Link
                  to={link.href}
                  onClick={() => setOpen(false)}
                  className={link.active ? 'font-semibold text-primary' : 'text-sub'}
                >
                  {link.label}
                </Link>
              </li>
            ))}
            <li className="flex gap-3 pt-2">
              <Button variant="secondary" size="sm" className="flex-1">
                <Link to="/login">Đăng nhập / Đăng ký</Link>
              </Button>
              <Button size="sm" className="flex-1">Đặt lịch khám</Button>
            </li>
          </ul>
        </nav>
      )}
    </header>
  )
}
