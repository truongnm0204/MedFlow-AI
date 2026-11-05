import { FileSearch, ShieldCheck, UserCog, Users } from 'lucide-react';

import type { SystemAdminMetricsDto } from '../types/system-admin.types';

interface SystemAdminStatCardsProps {
  metrics: SystemAdminMetricsDto;
}

export function SystemAdminStatCards({ metrics }: SystemAdminStatCardsProps) {
  const cards = [
    {
      key: 'users',
      label: 'Tài khoản toàn hệ thống',
      value: `${String(metrics.activeUsers).padStart(2, '0')}/${String(metrics.totalUsers).padStart(2, '0')}`,
      hint: `${metrics.lockedOrInactiveUsers} tài khoản khóa / ngừng hoạt động`,
      tone: 'border-sky-200 bg-sky-50 text-sky-700',
      icon: Users,
    },
    {
      key: 'staff',
      label: 'Tài khoản nhân sự y tế',
      value: `${String(metrics.activeStaffAccounts).padStart(2, '0')}/${String(metrics.totalStaffAccounts).padStart(2, '0')}`,
      hint: `${metrics.inactiveOrLockedStaffAccounts} hồ sơ nhân sự tạm khóa / nghỉ`,
      tone: 'border-emerald-200 bg-emerald-50 text-emerald-700',
      icon: UserCog,
    },
    {
      key: 'rbac',
      label: 'Vai trò & Quyền hạn (RBAC)',
      value: `${String(metrics.totalRoles).padStart(2, '0')} VT • ${String(metrics.totalPermissions).padStart(2, '0')} QH`,
      hint: 'Phân quyền truy cập 2 lớp toàn hệ thống',
      tone: 'border-amber-200 bg-amber-50 text-amber-700',
      icon: ShieldCheck,
    },
    {
      key: 'audit',
      label: 'Nhật ký kiểm toán (24h)',
      value: String(metrics.recentAuditCount24h).padStart(2, '0'),
      hint: `Tổng ${metrics.totalAuditLogs} bản ghi • ${metrics.totalHospitals} cơ sở / ${metrics.activeDepartments} khoa`,
      tone: 'border-border bg-white text-ink',
      icon: FileSearch,
    },
  ];

  return (
    <section
      aria-label="Chỉ số tổng quan quản trị hệ thống"
      className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4"
    >
      {cards.map((card) => {
        const Icon = card.icon;
        return (
          <div
            key={card.key}
            className={`flex items-start justify-between gap-3 rounded-2xl border p-4 shadow-2xs ${card.tone}`}
          >
            <div className="min-w-0 space-y-1">
              <div className="font-mono text-2xl font-bold whitespace-nowrap tabular-nums">
                {card.value}
              </div>
              <div className="text-xs font-bold">{card.label}</div>
              <div className="text-[11px] opacity-80">{card.hint}</div>
            </div>
            <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-current/15 bg-white/80 shadow-2xs">
              <Icon aria-hidden="true" className="h-5 w-5" />
            </span>
          </div>
        );
      })}
    </section>
  );
}
