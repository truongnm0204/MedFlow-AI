import { CheckCircle2, ClipboardList, Clock, PackageCheck } from 'lucide-react';

import type { PrescriptionSearchMetrics as MetricsType } from '../types/prescription-search.types';

interface PrescriptionSearchMetricsProps {
  metrics: MetricsType;
}

export function PrescriptionSearchMetrics({ metrics }: PrescriptionSearchMetricsProps) {
  const format2 = (n: number) => (n < 10 ? `0${n}` : `${n}`);

  const cards = [
    {
      title: 'CHỜ TIẾP NHẬN & SOẠN',
      value: format2(metrics.pendingApproval),
      subText: 'Ưu tiên cấp • Đơn đang cần duyệt',
      icon: ClipboardList,
      tone: 'text-amber-600',
      bg: 'bg-amber-50/50 border-amber-200/80',
    },
    {
      title: 'ĐANG CHUẨN BỊ CHIA THUỐC',
      value: format2(metrics.preparing),
      subText: '2 Dược sĩ phụ trách • TB: 4.2 phút/đơn',
      icon: Clock,
      tone: 'text-sky-600',
      bg: 'bg-sky-50/50 border-sky-200/80',
    },
    {
      title: 'ĐÃ SOẠN XONG - CHỜ GỌI',
      value: format2(metrics.readyForDispense),
      subText: 'Sẵn sàng phát • Đơn chờ bàn giao',
      icon: PackageCheck,
      tone: 'text-indigo-600',
      bg: 'bg-indigo-50/50 border-indigo-200/80',
    },
    {
      title: 'ĐÃ HOÀN TẤT TRONG CA',
      value: format2(metrics.completedInShift),
      subText: 'Đúng hạn: 98.5% • 0 sai sót GPP',
      icon: CheckCircle2,
      tone: 'text-emerald-600',
      bg: 'bg-emerald-50/50 border-emerald-200/80',
    },
  ];

  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
      {cards.map((c) => {
        const Icon = c.icon;
        return (
          <div key={c.title} className={`rounded-2xl border p-5 shadow-2xs space-y-1.5 ${c.bg}`}>
            <div className="flex items-center justify-between text-[11px] font-bold tracking-wider text-sub">
              <span>{c.title}</span>
              <Icon className={`h-4 w-4 ${c.tone}`} aria-hidden="true" />
            </div>
            <div className="text-3xl font-black text-navy font-mono">{c.value}</div>
            <div className="text-[11px] font-medium text-sub">{c.subText}</div>
          </div>
        );
      })}
    </div>
  );
}
