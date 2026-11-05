import type { RoleMetrics } from '../types/role.types';

interface RoleMetricsProps {
  metrics: RoleMetrics;
}

export function RoleMetricsCards({ metrics }: RoleMetricsProps) {
  const cards = [
    {
      label: 'TỔNG SỐ VAI TRÒ',
      value: metrics.totalRoles,
      unit: 'Vai trò',
      note: '07 vai trò cốt lõi hệ thống',
      tone: 'text-navy',
    },
    {
      label: 'ĐANG HOẠT ĐỘNG',
      value: metrics.activeRoles,
      unit: 'Vai trò',
      note: 'Đang áp dụng phân quyền RBAC',
      tone: 'text-emerald-600',
    },
    {
      label: 'TÀI KHOẢN ĐƯỢC GÁN',
      value: metrics.totalAssignedAccounts,
      unit: 'Tài khoản',
      note: 'Đồng bộ định danh toàn viện',
      tone: 'text-primary',
    },
    {
      label: 'QUYỀN HẠT NHÂN',
      value: metrics.totalPermissions,
      unit: 'Quyền hạn',
      note: 'Chuẩn bảo mật HL7 / ISO 27799',
      tone: 'text-amber-600',
    },
  ];

  return (
    <section
      aria-label="Chỉ số vai trò hệ thống"
      className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4"
    >
      {cards.map((card) => (
        <div key={card.label} className="rounded-xl border border-border bg-white p-5 shadow-sm">
          <p className="text-xs font-semibold tracking-wide text-sub">{card.label}</p>
          <p className={`mt-2 text-3xl font-bold ${card.tone}`}>
            {String(card.value).padStart(2, '0')}{' '}
            <span className="text-sm font-medium text-sub">{card.unit}</span>
          </p>
          <p className="mt-1 text-xs text-sub">{card.note}</p>
        </div>
      ))}
    </section>
  );
}
