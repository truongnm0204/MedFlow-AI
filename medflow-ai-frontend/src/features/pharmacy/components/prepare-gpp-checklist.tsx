import { ShieldCheck } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type { GppCheckItem } from '../types/prepare-medicine.types';

interface PrepareGppChecklistProps {
  checks: GppCheckItem[];
  onToggleCheck: (id: string) => void;
}

export function PrepareGppChecklist({ checks, onToggleCheck }: PrepareGppChecklistProps) {
  const checkedCount = checks.filter((c) => c.checked).length;

  return (
    <Card>
      <CardHeader className="flex flex-wrap items-center justify-between gap-2 py-3.5">
        <div className="flex items-center gap-2">
          <ShieldCheck className="h-4 w-4 text-emerald-600" aria-hidden="true" />
          <h2 className="text-sm font-bold text-navy">
            Bảng Kiểm Soát An Toàn Dược Lâm Sàng &amp; Đóng Gói (GPP Quality Check)
          </h2>
        </div>
        <Badge tone={checkedCount === checks.length ? 'emerald' : 'amber'}>
          {checkedCount}/{checks.length} Tiêu chuẩn đạt
        </Badge>
      </CardHeader>

      <CardContent className="grid grid-cols-1 gap-3 md:grid-cols-2">
        {checks.map((item) => (
          <label
            key={item.id}
            className={`flex cursor-pointer items-start gap-3 rounded-xl border p-3.5 transition-colors ${
              item.checked
                ? 'border-emerald-200 bg-emerald-50/40'
                : 'border-border bg-white hover:bg-canvas'
            }`}
          >
            <input
              type="checkbox"
              checked={item.checked}
              onChange={() => onToggleCheck(item.id)}
              className="mt-0.5 h-4 w-4 rounded border-slate-300 text-primary focus:ring-primary"
            />
            <div className="text-xs space-y-0.5">
              <div className="font-bold text-navy">{item.title}</div>
              <p className="text-[11px] leading-relaxed text-sub">{item.description}</p>
            </div>
          </label>
        ))}
      </CardContent>
    </Card>
  );
}
