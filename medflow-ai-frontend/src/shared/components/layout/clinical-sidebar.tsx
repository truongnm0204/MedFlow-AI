import {
  Activity,
  AlertCircle,
  BookOpen,
  Building2,
  Calendar,
  ChevronLeft,
  ChevronRight,
  CreditCard,
  DoorOpen,
  FileText,
  LayoutDashboard,
  Microscope,
  Pill,
  ShieldCheck,
  Stethoscope,
  UserCheck,
  Users,
} from 'lucide-react';
import type { ElementType } from 'react';
import { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { hasAnyRole } from '@/features/auth/utils/rbac';

import { MedflowLogo } from './medflow-logo';

export interface SidebarNavItem {
  id: string;
  label: string;
  icon: ElementType;
  href?: string;
  badge?: string;
  badgeColor?: 'sky' | 'amber' | 'emerald';
  hotkey?: string;
}

export interface ClinicalSidebarProps {
  activeItem?: string;
  onItemSelect?: (item: string) => void;
  variant?: 'clinical' | 'admin';
  className?: string;
}

export function ClinicalSidebar({
  activeItem = 'queue',
  onItemSelect,
  variant = 'clinical',
  className = '',
}: ClinicalSidebarProps) {
  const [isCollapsed, setIsCollapsed] = useState(false);
  const navigate = useNavigate();

  const user = useAuthStore((state) => state.user);
  const userRoles = user?.roles ?? [];
  const canAccessAdminPortal = hasAnyRole(userRoles, ['HOSPITAL_ADMIN', 'SYSTEM_ADMIN']);

  const isAdmin = variant === 'admin';
  const displayName =
    user?.fullName?.trim() ||
    user?.email?.split('@')[0] ||
    (isAdmin ? 'Quản trị cơ sở' : 'Bác sĩ lâm sàng');
  const roleSubLabel = user?.staffCode?.trim()
    ? `Mã NV: ${user.staffCode.trim()}`
    : isAdmin
      ? 'Quản trị hệ thống HIS'
      : 'Bàn khám ngoại trú';

  // Admin navigation structure
  const adminItems: SidebarNavItem[] = [
    {
      id: 'doctors',
      label: 'Bác sĩ & Nhân sự Y tế',
      icon: Users,
      href: '/admin/doctors',
      hotkey: 'F1',
    },
    {
      id: 'services',
      label: 'Danh mục Dịch vụ & Kỹ thuật',
      icon: Activity,
      href: '/admin/services',
      hotkey: 'F2',
    },
    {
      id: 'departments',
      label: 'Khoa phòng',
      icon: Building2,
      href: '/admin/departments',
    },
    {
      id: 'rooms',
      label: 'Buồng khám & Kỹ thuật',
      icon: DoorOpen,
      href: '/admin/rooms',
    },
    {
      id: 'pricing',
      label: 'Bảng giá & Biểu phí',
      icon: CreditCard,
      href: '/admin/pricing',
    },
    {
      id: 'slots',
      label: 'Quản lý Slot khám',
      icon: Calendar,
      href: '/admin/slots',
    },
    {
      id: 'policies',
      label: 'Chính sách hẹn',
      icon: ShieldCheck,
      href: '/admin/policies',
    },
  ];

  const adminSecondaryItems: SidebarNavItem[] = [
    {
      id: 'doctor-workbench',
      label: 'Bàn khám Bác sĩ',
      icon: LayoutDashboard,
      href: '/doctor',
    },
    {
      id: 'reception-desk',
      label: 'Bàn lễ tân Tiếp đón',
      icon: UserCheck,
      href: '/reception',
    },
    {
      id: 'diagnostic-desk',
      label: 'Kỹ thuật Cận lâm sàng',
      icon: Microscope,
      href: '/diagnostic',
    },
  ];

  // Clinical navigation structure
  const clinicalItems: SidebarNavItem[] = [
    {
      id: 'queue',
      label: 'Hàng đợi khám',
      icon: Users,
      hotkey: 'F1',
    },
    {
      id: 'emr',
      label: 'Hồ sơ bệnh án EMR',
      icon: FileText,
    },
    {
      id: 'consult',
      label: 'Khám & Kê đơn',
      icon: Stethoscope,
      hotkey: 'F2',
    },
    {
      id: 'diagnostics',
      label: 'Chỉ định CLS & PACS',
      icon: Microscope,
    },
    {
      id: 'prescription',
      label: 'Kê đơn thuốc điện tử',
      icon: Pill,
      hotkey: 'F4',
    },
  ];

  const clinicalToolItems: SidebarNavItem[] = [
    {
      id: 'icd10',
      label: 'Từ điển ICD-10 chuẩn',
      icon: BookOpen,
    },
    {
      id: 'drugs',
      label: 'Tra cứu tương tác thuốc',
      icon: AlertCircle,
    },
    ...(canAccessAdminPortal
      ? [
          {
            id: 'admin-portal',
            label: 'Cổng Quản trị Cơ sở',
            icon: ShieldCheck,
            href: '/admin/services',
          },
        ]
      : []),
  ];

  const primaryItems = isAdmin ? adminItems : clinicalItems;
  const secondaryItems = isAdmin ? adminSecondaryItems : clinicalToolItems;

  const primarySectionTitle = isAdmin ? 'Danh Mục Quản Trị' : 'Nghiệp Vụ Bàn Khám';
  const secondarySectionTitle = isAdmin ? 'Điều Hành Bệnh Viện' : 'Công Cụ Lâm Sàng';

  // Keyboard navigation for hotkeys
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (isAdmin) {
        if (event.key === 'F1') {
          event.preventDefault();
          navigate('/admin/doctors');
        }
        if (event.key === 'F2') {
          event.preventDefault();
          navigate('/admin/services');
        }
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isAdmin, navigate]);

  const renderNavItem = (item: SidebarNavItem) => {
    const Icon = item.icon;
    const isActive = activeItem === item.id;

    const content = (
      <>
        <div className="flex items-center gap-2.5 min-w-0">
          <Icon
            className={`h-4 w-4 shrink-0 transition-colors ${
              isActive ? 'text-white' : 'text-slate-400 group-hover:text-slate-700'
            }`}
            aria-hidden="true"
          />
          {!isCollapsed && <span className="truncate">{item.label}</span>}
        </div>

        {!isCollapsed && (
          <div className="flex items-center gap-1.5 shrink-0">
            {item.badge && (
              <span
                className={`px-1.5 py-0.5 rounded-full text-[10px] font-mono font-bold ${
                  isActive
                    ? 'bg-white/20 text-white'
                    : item.badgeColor === 'amber'
                      ? 'bg-amber-50 text-amber-700 border border-amber-200'
                      : 'bg-sky-50 text-primary border border-sky-200'
                }`}
              >
                {item.badge}
              </span>
            )}
            {item.hotkey && (
              <kbd
                className={`text-[9px] font-mono px-1 py-0.5 rounded border ${
                  isActive
                    ? 'bg-white/20 border-white/30 text-white'
                    : 'bg-slate-100 border-slate-200 text-slate-400'
                }`}
              >
                {item.hotkey}
              </kbd>
            )}
          </div>
        )}
      </>
    );

    const commonClass = `group w-full flex items-center justify-between px-3 py-2 rounded-xl text-xs font-medium transition-all ${
      isActive
        ? 'bg-primary text-white shadow-sm shadow-primary/20 font-semibold'
        : 'text-slate-600 hover:text-slate-900 hover:bg-slate-50'
    }`;

    if (item.href) {
      return (
        <Link
          key={item.id}
          to={item.href}
          onClick={() => onItemSelect?.(item.id)}
          className={commonClass}
          title={isCollapsed ? item.label : undefined}
        >
          {content}
        </Link>
      );
    }

    return (
      <button
        key={item.id}
        type="button"
        onClick={() => onItemSelect?.(item.id)}
        className={commonClass}
        title={isCollapsed ? item.label : undefined}
      >
        {content}
      </button>
    );
  };

  return (
    <aside
      aria-label={isAdmin ? 'Điều hướng quản trị' : 'Điều hướng lâm sàng'}
      className={`bg-white border-r border-border flex flex-col justify-between transition-all duration-200 select-none ${
        isCollapsed ? 'w-[68px]' : 'w-[260px]'
      } ${className}`}
    >
      {/* Top Header & Role */}
      <div>
        {/* Brand Bar */}
        <div
          className={`relative h-16 border-b border-border flex items-center ${
            isCollapsed ? 'justify-center px-2' : 'justify-between px-4'
          }`}
        >
          <div className="flex items-center gap-2.5 overflow-hidden">
            <MedflowLogo
              variant={isCollapsed ? 'icon' : 'full'}
              className={isCollapsed ? 'h-8 w-auto shrink-0' : 'h-9 w-auto shrink-0'}
              linkToHome={false}
            />
            {!isCollapsed && (
              <span className="rounded bg-sky-50 px-1.5 py-0.5 text-[9px] font-bold text-primary font-mono shrink-0">
                {isAdmin ? 'ADMIN' : 'CS1'}
              </span>
            )}
          </div>

          <button
            type="button"
            onClick={() => setIsCollapsed(!isCollapsed)}
            aria-label={isCollapsed ? 'Mở rộng thanh bên' : 'Thu gọn thanh bên'}
            aria-expanded={!isCollapsed}
            className={
              isCollapsed
                ? 'absolute -right-3 top-1/2 -translate-y-1/2 z-20 flex h-6 w-6 items-center justify-center rounded-full border border-border bg-white text-slate-500 shadow-xs hover:bg-slate-100 hover:text-slate-700 transition-colors'
                : 'p-1 rounded-md text-slate-400 hover:text-slate-700 hover:bg-slate-100 transition-colors'
            }
            title={isCollapsed ? 'Mở rộng' : 'Thu gọn'}
          >
            {isCollapsed ? (
              <ChevronRight className="h-4 w-4" aria-hidden="true" />
            ) : (
              <ChevronLeft className="h-4 w-4" aria-hidden="true" />
            )}
          </button>
        </div>

        {/* Role Identity Card */}
        {!isCollapsed && (
          <div className="m-3 flex items-center gap-2.5 rounded-xl border border-sky-100 bg-sky-50/70 p-3">
            <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-sky-200 bg-sky-100 text-xs font-bold text-primary">
              {isAdmin ? 'AD' : 'LS'}
            </div>
            <div className="min-w-0 flex-1">
              <div className="truncate font-mono text-[9px] font-bold tracking-wider text-primary uppercase">
                {isAdmin ? 'QUẢN TRỊ CƠ SỞ' : 'Doctor Workbench'}
              </div>
              <div className="truncate text-xs font-bold text-ink">{displayName}</div>
              <div className="truncate text-[10px] text-sub">{roleSubLabel}</div>
            </div>
          </div>
        )}

        {/* Navigation Group 1 */}
        <div className="space-y-1 px-3 py-2">
          {!isCollapsed && (
            <div className="mb-1 px-2 font-mono text-[10px] font-bold tracking-wider text-sub uppercase">
              {primarySectionTitle}
            </div>
          )}
          {primaryItems.map(renderNavItem)}
        </div>

        {/* Navigation Group 2 */}
        <div className="space-y-1 px-3 py-2">
          {!isCollapsed && (
            <div className="mb-1 px-2 font-mono text-[10px] font-bold tracking-wider text-sub uppercase">
              {secondarySectionTitle}
            </div>
          )}
          {secondaryItems.map(renderNavItem)}
        </div>
      </div>

      {/* Footer Info */}
      <div className="border-t border-border bg-canvas p-3">
        {!isCollapsed ? (
          <div className="space-y-1 rounded-lg border border-border bg-white p-2 text-[10px] shadow-2xs">
            <div className="flex items-center justify-between font-semibold text-ink">
              <span>{isAdmin ? 'Quản trị hệ thống HIS' : 'Bàn khám Ngoại trú'}</span>
            </div>
            <div className="flex items-center justify-between font-mono text-sub">
              <span>100% Non-BHYT</span>
              <span className="font-semibold text-primary">Hotline: 1900 6868</span>
            </div>
          </div>
        ) : (
          <div className="flex justify-center font-mono text-[9px] font-bold text-sub">
            {isAdmin ? 'ADM' : 'HIS'}
          </div>
        )}
      </div>
    </aside>
  );
}
