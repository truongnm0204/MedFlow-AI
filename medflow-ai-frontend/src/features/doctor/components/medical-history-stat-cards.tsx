import { ClipboardCheck, FileText, FlaskConical, Pill } from 'lucide-react';

import { Card, CardContent } from '@/shared/ui/card';

import type { MedicalHistoryStatsDto } from '../types/medical-history.types';

export interface MedicalHistoryStatCardsProps {
  stats: MedicalHistoryStatsDto;
}

function formatLastVisitDate(lastVisitAt: string | null): string {
  if (!lastVisitAt) return 'Chưa có lượt khám trước';
  const parsed = new Date(lastVisitAt);
  if (Number.isNaN(parsed.getTime())) return `Khám gần nhất: ${lastVisitAt}`;
  return `Khám gần nhất: ${parsed.toLocaleDateString('vi-VN')}`;
}

export function MedicalHistoryStatCards({ stats }: MedicalHistoryStatCardsProps) {
  const cards = [
    {
      key: 'totalVisits',
      label: 'Tổng lượt khám',
      value: stats.totalVisits,
      subtext: `${stats.completedVisits} lượt hoàn tất`,
      icon: FileText,
      iconStyle: 'bg-sky-50 text-sky-600 border-sky-200',
    },
    {
      key: 'finalizedRecords',
      label: 'Bệnh án đã chốt',
      value: stats.finalizedRecordsCount,
      subtext: 'Hồ sơ khám đã xác nhận',
      icon: ClipboardCheck,
      iconStyle: 'bg-emerald-50 text-emerald-600 border-emerald-200',
    },
    {
      key: 'diagnosesAndOrders',
      label: 'Chẩn đoán & Cận lâm sàng',
      value: stats.totalDiagnosesCount + stats.totalDiagnosticOrdersCount,
      subtext: `${stats.totalDiagnosesCount} ICD-10 • ${stats.totalDiagnosticOrdersCount} chỉ định CLS`,
      icon: FlaskConical,
      iconStyle: 'bg-amber-50 text-amber-600 border-amber-200',
    },
    {
      key: 'prescriptions',
      label: 'Đơn thuốc đã kê',
      value: stats.totalPrescriptionsCount,
      subtext: formatLastVisitDate(stats.lastVisitAt),
      icon: Pill,
      iconStyle: 'bg-rose-50 text-rose-600 border-rose-200',
    },
  ];

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4">
      {cards.map((card) => {
        const Icon = card.icon;
        return (
          <Card key={card.key}>
            <CardContent className="p-4 flex items-start justify-between gap-3">
              <div className="space-y-1 min-w-0">
                <div className="text-xs font-semibold text-sub">{card.label}</div>
                <div className="text-2xl font-bold font-mono tabular-nums text-navy">
                  {card.value}
                </div>
                <div className="text-[11px] text-sub truncate">{card.subtext}</div>
              </div>
              <div
                className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border ${card.iconStyle}`}
              >
                <Icon className="h-5 w-5" aria-hidden="true" />
              </div>
            </CardContent>
          </Card>
        );
      })}
    </div>
  );
}
