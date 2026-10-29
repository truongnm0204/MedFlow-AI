import type { StaffAccountMetrics } from '../types/staff-account.types';

interface StaffAccountMetricsProps {
  metrics: StaffAccountMetrics;
}

const pad = (value: number) => String(value).padStart(2, '0');

export function StaffAccountMetricsCards({ metrics }: StaffAccountMetricsProps) {
  const cards = [
    { label: 'TÀI KHOẢN NHÂN SỰ', value: metrics.total, note: 'Theo danh mục nhân sự', tone: 'text-navy' },
    { label: 'ĐANG HOẠT ĐỘNG', value: metrics.active, note: 'Đăng nhập được cấp phép', tone: 'text-emerald-600' },
    { label: 'ĐÃ KHÓA', value: metrics.locked, note: 'Không thể đăng nhập', tone: 'text-amber-600' },
    { label: 'ĐÃ VÔ HIỆU HÓA', value: metrics.disabled, note: 'Lịch sử vẫn được lưu', tone: 'text-sub' },
  ];

  return (
    <section aria-label="Chỉ số tài khoản nhân sự" className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
      {cards.map((card) => (
        <div key={card.label} className="rounded-xl border border-border bg-white p-5 shadow-sm">
          <p className="text-xs font-semibold tracking-wide text-sub">{card.label}</p>
          <p className={`mt-2 text-3xl font-bold ${card.tone}`}>
            {pad(card.value)} <span className="text-sm font-medium text-sub">Tài khoản</span>
          </p>
          <p className="mt-1 text-xs text-sub">{card.note}</p>
        </div>
      ))}
    </section>
  );
}
