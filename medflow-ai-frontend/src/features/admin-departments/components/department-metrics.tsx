import type { DepartmentMetrics } from '../types/department.types';

interface DepartmentMetricsProps {
  metrics: DepartmentMetrics;
}

const pad = (n: number) => String(n).padStart(2, '0');

export function DepartmentMetricsCards({ metrics }: DepartmentMetricsProps) {
  const cards = [
    {
      label: 'TỔNG SỐ KHOA',
      value: String(metrics.total),
      unit: 'Khoa',
      hint: `${metrics.active} khoa đang hoạt động`,
    },
    {
      label: 'ĐANG HOẠT ĐỘNG',
      value: String(metrics.active),
      unit: 'Khoa',
      hint: 'Theo trạng thái danh mục',
    },
    {
      label: 'TẠM NGỪNG',
      value: pad(metrics.inactive),
      unit: 'Khoa',
      hint: 'Không dùng vào lịch mới',
    },
    {
      label: 'CƠ SỞ QUẢN LÝ',
      value: pad(metrics.facilities),
      unit: 'cơ sở',
      hint: 'Theo cơ sở',
    },
  ];

  return (
    <section aria-label="Thống kê khoa phòng" className="grid grid-cols-1 gap-4 md:grid-cols-4">
      {cards.map((card) => (
        <div key={card.label} className="rounded-xl border border-border bg-white p-4 shadow-sm">
          <p className="text-xs font-semibold tracking-wide text-sub">{card.label}</p>
          <p className="mt-2 text-navy">
            <span className="text-3xl font-bold">{card.value}</span>{' '}
            <span className="text-sm font-medium">{card.unit}</span>
          </p>
          <p className="mt-1 text-xs text-sub">{card.hint}</p>
        </div>
      ))}
    </section>
  );
}
