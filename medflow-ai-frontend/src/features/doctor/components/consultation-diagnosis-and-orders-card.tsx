import { FlaskConical, Pill, Stethoscope } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type {
  ConsultationDiagnosisItemDto,
  ConsultationDiagnosticSummaryDto,
  ConsultationPrescriptionSummaryDto,
} from '../types/consultation-workspace.types';

export interface ConsultationDiagnosisAndOrdersCardProps {
  diagnoses: ConsultationDiagnosisItemDto[];
  diagnosticSummary: ConsultationDiagnosticSummaryDto;
  prescriptionSummary: ConsultationPrescriptionSummaryDto;
}

export function ConsultationDiagnosisAndOrdersCard({
  diagnoses,
  diagnosticSummary,
  prescriptionSummary,
}: ConsultationDiagnosisAndOrdersCardProps) {
  return (
    <Card>
      <CardHeader className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Stethoscope className="h-4 w-4 text-primary" aria-hidden="true" />
          <div>
            <h3 className="text-sm font-bold text-navy">
              Chẩn đoán ICD-10, Cận lâm sàng & Đơn thuốc (UC34 / UC36 / UC37)
            </h3>
            <p className="text-[11px] text-sub">
              Tổng hợp chẩn đoán hiện tại, trạng thái hàng đợi CLS và đơn thuốc ngoại trú
            </p>
          </div>
        </div>
        <Badge tone={diagnosticSummary.allMandatoryResultsReady ? 'emerald' : 'amber'}>
          {diagnosticSummary.allMandatoryResultsReady
            ? 'Đủ điều kiện kết luận CLS'
            : `Chờ ${diagnosticSummary.openQueueEntries} kết quả CLS`}
        </Badge>
      </CardHeader>

      <CardContent className="space-y-5">
        <div>
          <div className="text-xs font-bold text-navy mb-2">Danh sách chẩn đoán ICD-10</div>
          {diagnoses.length === 0 ? (
            <div className="rounded-xl bg-canvas border border-border px-4 py-5 text-center text-xs text-sub">
              Chưa ghi nhận chẩn đoán ICD-10 cho lượt khám này.
            </div>
          ) : (
            <div className="space-y-2">
              {diagnoses.map((diag) => (
                <div
                  key={diag.id}
                  className="rounded-xl bg-canvas border border-border p-3 flex flex-col sm:flex-row sm:items-center justify-between gap-2 text-xs"
                >
                  <div className="space-y-0.5">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="rounded-md bg-primary/10 text-primary px-2 py-0.5 font-mono font-extrabold">
                        {diag.code}
                      </span>
                      <span className="font-bold text-navy">{diag.name}</span>
                      <Badge tone={diag.isPrimary ? 'sky' : 'slate'}>
                        {diag.isPrimary ? 'Bệnh chính' : 'Bệnh kèm theo'}
                      </Badge>
                    </div>
                    {diag.clinicalDiagnosis && (
                      <div className="text-sub">Chẩn đoán lâm sàng: {diag.clinicalDiagnosis}</div>
                    )}
                  </div>
                  <Badge tone={diag.status === 'CONFIRMED' ? 'emerald' : 'amber'}>
                    {diag.status === 'CONFIRMED' ? 'Chẩn đoán xác định' : 'Chẩn đoán sơ bộ'}
                  </Badge>
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-xs">
          <div className="rounded-xl bg-canvas border border-border p-3.5 space-y-2">
            <div className="flex items-center justify-between">
              <div className="font-bold text-navy flex items-center gap-1.5">
                <FlaskConical className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                <span>Tóm tắt Cận lâm sàng (CLS)</span>
              </div>
              <Badge tone={diagnosticSummary.openQueueEntries > 0 ? 'amber' : 'emerald'}>
                {diagnosticSummary.completedQueueEntries}/{diagnosticSummary.totalQueueEntries} hoàn
                tất
              </Badge>
            </div>
            <div className="grid grid-cols-3 gap-2 text-center">
              <div className="rounded-lg bg-white border border-border p-2">
                <div className="text-[10px] text-sub">Tổng phiếu</div>
                <div className="font-mono text-sm font-extrabold text-navy">
                  {diagnosticSummary.totalQueueEntries}
                </div>
              </div>
              <div className="rounded-lg bg-white border border-border p-2">
                <div className="text-[10px] text-sub">Đang chờ</div>
                <div className="font-mono text-sm font-extrabold text-amber-700">
                  {diagnosticSummary.openQueueEntries}
                </div>
              </div>
              <div className="rounded-lg bg-white border border-border p-2">
                <div className="text-[10px] text-sub">Đã có KQ</div>
                <div className="font-mono text-sm font-extrabold text-emerald-700">
                  {diagnosticSummary.completedQueueEntries}
                </div>
              </div>
            </div>
          </div>

          <div className="rounded-xl bg-canvas border border-border p-3.5 space-y-2">
            <div className="flex items-center justify-between">
              <div className="font-bold text-navy flex items-center gap-1.5">
                <Pill className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                <span>Đơn thuốc ngoại trú</span>
              </div>
              <Badge tone={prescriptionSummary.totalPrescriptions > 0 ? 'emerald' : 'slate'}>
                {prescriptionSummary.totalPrescriptions > 0
                  ? `${prescriptionSummary.totalPrescriptions} đơn thuốc`
                  : 'Chưa kê đơn'}
              </Badge>
            </div>
            {prescriptionSummary.latestPrescriptionCode ? (
              <div className="rounded-lg bg-white border border-border p-2.5 space-y-1">
                <div className="flex items-center justify-between">
                  <span className="font-mono font-bold text-navy">
                    {prescriptionSummary.latestPrescriptionCode}
                  </span>
                  <span className="font-mono text-[11px] text-sub">
                    {prescriptionSummary.itemCount} khoản thuốc
                  </span>
                </div>
                <div className="text-[11px] text-sub">
                  Trạng thái: <strong>{prescriptionSummary.latestPrescriptionStatus}</strong>
                </div>
              </div>
            ) : (
              <div className="rounded-lg bg-white border border-border p-3 text-sub">
                Chưa có đơn thuốc nào được phát hành cho lượt khám này.
              </div>
            )}
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
