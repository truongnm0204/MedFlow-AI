import { AlertTriangle, Pill, ShieldCheck } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type {
  DispenseMedicationCounselingItem,
  PharmacistHandoverCheck,
} from '../types/dispense-confirm.types';

interface DispenseMedicationCounselingListProps {
  regimenBadge: string;
  medications: DispenseMedicationCounselingItem[];
  checks: PharmacistHandoverCheck[];
  onToggleCheck: (checkId: string) => void;
}

export function DispenseMedicationCounselingList({
  regimenBadge,
  medications,
  checks,
  onToggleCheck,
}: DispenseMedicationCounselingListProps) {
  return (
    <div className="space-y-4">
      <Card>
        <CardHeader className="space-y-2 py-3.5">
          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-2">
              <Pill className="h-4 w-4 text-primary" aria-hidden="true" />
              <h2 className="text-sm font-bold text-navy">
                3. Tư vấn &amp; Hướng dẫn Sử dụng Thuốc Trực tiếp (Chuẩn GPP)
              </h2>
            </div>
            <Badge tone="sky">{medications.length} khoản thuốc</Badge>
          </div>
          <div className="rounded-lg bg-navy px-3 py-1.5 text-xs font-bold text-white">
            {regimenBadge}
          </div>
        </CardHeader>

        <CardContent className="space-y-3">
          {medications.map((med) => (
            <div
              key={med.id}
              className="rounded-xl border border-border bg-white p-3.5 space-y-1.5 text-xs"
            >
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="font-bold text-navy">
                  0{med.orderIndex}. {med.drugName}{' '}
                  <span className="font-mono text-primary">({med.qtyLabel})</span>
                </div>
                {med.tagLabel && <Badge tone={med.tagTone ?? 'sky'}>{med.tagLabel}</Badge>}
              </div>

              <div className="font-mono text-[11px] text-sub">
                Lô: <strong>{med.batchCode}</strong> • HSD: {med.expiryDate}
              </div>

              <p className="font-medium text-ink">
                <strong className="text-navy">Liều dùng: </strong>
                {med.dosageInstruction}
              </p>

              <p
                className={`flex items-start gap-1.5 rounded-lg px-2.5 py-1.5 text-[11px] font-medium ${
                  med.tagTone === 'rose'
                    ? 'bg-rose-50 text-rose-700 border border-rose-200'
                    : 'bg-amber-50/70 text-amber-900 border border-amber-200/80'
                }`}
              >
                <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                <span>{med.warningNote}</span>
              </p>
            </div>
          ))}
        </CardContent>
      </Card>

      {/* Compulsory Pharmacist Checklist */}
      <Card>
        <CardHeader className="py-3">
          <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wide text-navy">
            <ShieldCheck className="h-4 w-4 text-emerald-600" aria-hidden="true" />
            <span>Checklist Bắt buộc của Dược sĩ Bàn giao</span>
          </div>
        </CardHeader>
        <CardContent className="space-y-2.5">
          {checks.map((chk) => (
            <label
              key={chk.id}
              className="flex cursor-pointer items-start gap-2.5 rounded-lg border border-border bg-canvas p-3 text-xs font-medium text-navy hover:bg-slate-100"
            >
              <input
                type="checkbox"
                checked={chk.checked}
                onChange={() => onToggleCheck(chk.id)}
                className="mt-0.5 h-4 w-4 rounded border-slate-300 text-primary focus:ring-primary"
              />
              <span>{chk.label}</span>
            </label>
          ))}
        </CardContent>
      </Card>
    </div>
  );
}
