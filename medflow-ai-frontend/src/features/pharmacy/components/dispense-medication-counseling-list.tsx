import { AlertTriangle, Pill, ShieldCheck } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type {
  DispenseMedicationCounselingItem,
  PharmacistHandoverCheck,
} from '../types/dispense-confirm.types';

export interface DispenseLineEditState {
  dispensedQuantity?: number;
  shortageReason?: string;
}

interface DispenseMedicationCounselingListProps {
  regimenBadge: string;
  medications: DispenseMedicationCounselingItem[];
  checks: PharmacistHandoverCheck[];
  onToggleCheck: (checkId: string) => void;
  lineEdits?: Record<string, DispenseLineEditState>;
  onChangeLineQuantity?: (medId: string, quantity: number) => void;
  onChangeShortageReason?: (medId: string, reason: string) => void;
  disabled?: boolean;
}

export function DispenseMedicationCounselingList({
  regimenBadge,
  medications,
  checks,
  onToggleCheck,
  lineEdits,
  onChangeLineQuantity,
  onChangeShortageReason,
  disabled = false,
}: DispenseMedicationCounselingListProps) {
  // Compute total dispensed per prescriptionItemId to know when shortageReason is required
  const totalsByPrescriptionItemId = new Map<number, { prescribed: number; dispensed: number }>();
  for (const med of medications) {
    if (med.prescriptionItemId == null) continue;
    const defaultQty = med.dispensedQuantity ?? med.prescribedQuantity ?? 0;
    const effectiveQty = lineEdits?.[med.id]?.dispensedQuantity ?? defaultQty;
    const prev = totalsByPrescriptionItemId.get(med.prescriptionItemId);
    totalsByPrescriptionItemId.set(med.prescriptionItemId, {
      prescribed: med.prescribedQuantity ?? prev?.prescribed ?? 0,
      dispensed: (prev?.dispensed ?? 0) + effectiveQty,
    });
  }

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
          {medications.map((med) => {
            const defaultQty = med.dispensedQuantity ?? med.prescribedQuantity ?? 0;
            const currentQty = lineEdits?.[med.id]?.dispensedQuantity ?? defaultQty;
            const currentReason = lineEdits?.[med.id]?.shortageReason ?? '';
            const itemTotals =
              med.prescriptionItemId != null
                ? totalsByPrescriptionItemId.get(med.prescriptionItemId)
                : undefined;
            const requiresShortageReason =
              itemTotals !== undefined && itemTotals.dispensed < itemTotals.prescribed;

            return (
              <div
                key={med.id}
                className="space-y-2 rounded-xl border border-border bg-white p-3.5 text-xs"
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

                {onChangeLineQuantity ? (
                  <div className="grid grid-cols-1 gap-2.5 pt-1 sm:grid-cols-12">
                    <div className="sm:col-span-4">
                      <label
                        htmlFor={`dispense-qty-${med.id}`}
                        className="block text-[11px] font-semibold text-sub"
                      >
                        Số lượng thực xuất ({med.unit ?? 'đơn vị'})
                      </label>
                      <input
                        id={`dispense-qty-${med.id}`}
                        type="number"
                        min={0}
                        max={med.prescribedQuantity ?? undefined}
                        step="any"
                        disabled={disabled}
                        value={currentQty}
                        onChange={(e) => {
                          const parsed = Number(e.target.value);
                          onChangeLineQuantity(med.id, Number.isFinite(parsed) ? parsed : 0);
                        }}
                        className="mt-1 w-full rounded-lg border border-border bg-canvas px-2.5 py-1.5 font-mono text-xs font-semibold text-ink focus:border-primary focus:outline-none disabled:opacity-60"
                      />
                    </div>

                    {requiresShortageReason && onChangeShortageReason ? (
                      <div className="sm:col-span-8">
                        <label
                          htmlFor={`shortage-reason-${med.id}`}
                          className="block text-[11px] font-semibold text-amber-800"
                        >
                          Lý do cấp thiếu / bằng 0 (bắt buộc)
                        </label>
                        <input
                          id={`shortage-reason-${med.id}`}
                          type="text"
                          disabled={disabled}
                          value={currentReason}
                          placeholder="Nhập lý do cấp thiếu thuốc hoặc hết hàng tạm thời..."
                          onChange={(e) => onChangeShortageReason(med.id, e.target.value)}
                          className="mt-1 w-full rounded-lg border border-amber-300 bg-white px-2.5 py-1.5 text-xs text-ink focus:border-primary focus:outline-none disabled:opacity-60"
                        />
                      </div>
                    ) : null}
                  </div>
                ) : null}

                <p className="font-medium text-ink">
                  <strong className="text-navy">Liều dùng: </strong>
                  {med.dosageInstruction}
                </p>

                <p
                  className={`flex items-start gap-1.5 rounded-lg px-2.5 py-1.5 text-[11px] font-medium ${
                    med.tagTone === 'rose'
                      ? 'border border-rose-200 bg-rose-50 text-rose-700'
                      : 'border border-amber-200/80 bg-amber-50/70 text-amber-900'
                  }`}
                >
                  <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                  <span>{med.warningNote}</span>
                </p>
              </div>
            );
          })}
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
