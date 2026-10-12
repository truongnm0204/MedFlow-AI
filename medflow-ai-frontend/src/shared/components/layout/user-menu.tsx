import { Link } from 'react-router-dom'
import {
  CalendarCheck,
  ChevronDown,
  FileText,
  FolderHeart,
  Search,
  Settings,
  UserRound,
  LogOut,
} from 'lucide-react'
import { Popover } from '../../ui/popover'
import { Avatar } from '../../ui/avatar'
import { Badge } from '../../ui/badge'

export interface UserMenuUser {
  fullName?: string | null
  email?: string | null
  patientCode?: string | null
}

interface UserMenuProps {
  user: UserMenuUser | null
  /** Số lịch hẹn sắp tới — hiển thị badge trên mục "Lịch hẹn của tôi". */
  upcomingAppointments?: number
  onLogout?: () => void
}

const ITEMS = [
  { icon: CalendarCheck, label: 'Lịch hẹn của tôi', to: '/appointments', badge: true },
  { icon: Search, label: 'Tìm bác sĩ & dịch vụ', to: '/#doctors' },
  { icon: FileText, label: 'Hồ sơ bệnh án EMR', to: '/emr' },
  { icon: FolderHeart, label: 'Hồ sơ gia đình', to: '/family' },
  { icon: UserRound, label: 'Thông tin cá nhân', to: '/profile' },
  { icon: Settings, label: 'Cài đặt tài khoản', to: '/settings', divider: true },
]

/** Menu người dùng theo Figma `10:139` (chip avatar + popover items). */
export function UserMenu({ user, upcomingAppointments = 0, onLogout }: UserMenuProps) {
  const displayName = user?.fullName ?? 'Khách'

  return (
    <Popover
      trigger={(open) => (
        <span
          className="flex cursor-pointer items-center gap-2.5 rounded-[10px] border border-border bg-white py-1 pl-2 pr-3 transition-colors hover:bg-canvas"
          aria-label="Menu tài khoản"
          aria-expanded={open}
        >
          <Avatar name={displayName} size="sm" />
          <span className="text-[13px] font-semibold text-navy">{displayName}</span>
          <ChevronDown className={`h-3.5 w-3.5 text-slate-400 transition-transform ${open ? 'rotate-180' : ''}`} />
        </span>
      )}
    >
      <div className="border-b border-border bg-canvas px-3 py-2">
        <p className="text-[11px] font-bold uppercase tracking-wide text-sub">
          Tài khoản bệnh nhân
        </p>
        <p className="text-base font-semibold text-navy">{displayName}</p>
        <p className="font-mono text-xs text-sub">{user?.email ?? ''}</p>
        {user?.patientCode && (
          <p className="font-mono text-[11px] text-slate-400">Mã BN: {user.patientCode}</p>
        )}
      </div>

      <ul className="flex flex-col gap-0.5 py-1">
        {ITEMS.map(({ icon: Icon, label, to, badge, divider }) => (
          <li key={label} className={divider ? 'border-t border-border' : undefined}>
            <Link
              to={to}
              className={`flex items-center gap-2.5 rounded-lg px-3 py-1.5 text-sm transition-colors hover:bg-sky-50 ${
                badge && upcomingAppointments > 0
                  ? 'bg-primary/5 font-semibold text-primary'
                  : 'text-sub'
              }`}
            >
              <Icon className="h-4 w-4 shrink-0" />
              <span className="flex-1">{label}</span>
              {badge && upcomingAppointments > 0 && (
                <Badge solid>{upcomingAppointments}</Badge>
              )}
            </Link>
          </li>
        ))}
        <li className="border-t border-border">
          <button
            type="button"
            onClick={onLogout}
            className="flex w-full items-center gap-2.5 rounded-lg px-3 py-1.5 text-sm text-danger transition-colors hover:bg-rose-50"
          >
            <LogOut className="h-4 w-4" />
            Đăng xuất
          </button>
        </li>
      </ul>
    </Popover>
  )
}
