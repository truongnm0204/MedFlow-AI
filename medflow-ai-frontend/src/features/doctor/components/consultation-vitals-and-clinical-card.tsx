import { Activity, ClipboardList, FileText, HeartPulse, Thermometer, Weight } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type { ConsultationClinicalContextDto } from '../types/consultation-workspace.types';

export interface ConsultationVitalsAndClinicalCardProps {
  clinicalContext: ConsultationClinicalContextDto;
  canEditClinical: boolean;
}

const RECORD_STATUS_META: Record<
  string,
  { label: string; tone: 'sky' | 'emerald' | 'amber' | 'slate' }
> = {
  NOT_CREATED: { label: 'Chưa khởi tạo bệnh án', tone: 'slate' },
  DRAFT: { label: 'Bệnh án nháp (DRAFT)', tone: 'amber' },
  FINALIZED: { label: 'Đã khóa bệnh án (FINALIZED)', tone: 'emerald' },
};

export function ConsultationVitalsAndClinicalCard({
  clinicalContext,
  canEditClinical,
}: ConsultationVitalsAndClinicalCardProps) {
  const { vitalSigns } = clinicalContext;
  const recordStatusMeta = RECORD_STATUS_META[clinicalContext.medicalRecordStatus] ?? {
    label: clinicalContext.medicalRecordStatus,
    tone: 'slate' as const,
  };

  const bpDisplay =
    vitalSigns.bloodPressureLabel ??
    (vitalSigns.bpSystolicMmhg !== null && vitalSigns.bpDiastolicMmhg !== null
      ? `${vitalSigns.bpSystolicMmhg}/${vitalSigns.bpDiastolicMmhg} mmHg`
      : 'Chưa đo');

  const heightWeightDisplay =
    vitalSigns.heightCm !== null || vitalSigns.weightKg !== null
      ? `${vitalSigns.heightCm ?? '—'} cm / ${vitalSigns.weightKg ?? '—'} kg`
      : 'Chưa đo';

  return (
    <Card>
      <CardHeader className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <HeartPulse className="h-4 w-4 text-primary" aria-hidden="true" />
          <div>
            <h3 className="text-sm font-bold text-navy">
              Sinh hiệu & Hồ sơ khám lâm sàng (UC31 / UC33)
            </h3>
            <p className="text-[11px] text-sub">
              {clinicalContext.medicalRecordId
                ? `Mã BA #${clinicalContext.medicalRecordId} • Phiên bản #${clinicalContext.currentRevisionNo ?? 1}`
                : 'Hồ sơ bệnh án sẽ tự động khởi tạo ở trạng thái DRAFT khi bắt đầu khám'}
            </p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge tone={recordStatusMeta.tone}>{recordStatusMeta.label}</Badge>
          <Badge tone={canEditClinical ? 'emerald' : 'slate'}>
            {canEditClinical ? 'Cho phép ghi nhận lâm sàng' : 'Chỉ xem'}
          </Badge>
        </div>
      </CardHeader>

      <CardContent className="space-y-5">
        <div>
          <div className="text-xs font-bold text-navy mb-2.5 flex items-center gap-1.5">
            <Activity className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
            <span>Chỉ số sinh hiệu đầu vào</span>
          </div>
          <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-2.5">
            <div className="rounded-xl bg-canvas border border-border p-2.5">
              <div className="text-[10px] font-semibold uppercase text-sub">Huyết áp</div>
              <div className="mt-1 font-mono text-xs font-extrabold text-navy">{bpDisplay}</div>
            </div>
            <div className="rounded-xl bg-canvas border border-border p-2.5">
              <div className="text-[10px] font-semibold uppercase text-sub">Mạch</div>
              <div className="mt-1 font-mono text-xs font-extrabold text-navy">
                {vitalSigns.pulseBpm !== null ? `${vitalSigns.pulseBpm} bpm` : 'Chưa đo'}
              </div>
            </div>
            <div className="rounded-xl bg-canvas border border-border p-2.5">
              <div className="text-[10px] font-semibold uppercase text-sub flex items-center gap-1">
                <Thermometer className="h-3 w-3 text-amber-600" aria-hidden="true" />
                <span>Nhiệt độ</span>
              </div>
              <div className="mt-1 font-mono text-xs font-extrabold text-navy">
                {vitalSigns.temperatureC !== null ? `${vitalSigns.temperatureC} °C` : 'Chưa đo'}
              </div>
            </div>
            <div className="rounded-xl bg-canvas border border-border p-2.5">
              <div className="text-[10px] font-semibold uppercase text-sub">SpO2</div>
              <div className="mt-1 font-mono text-xs font-extrabold text-navy">
                {vitalSigns.spo2Percent !== null ? `${vitalSigns.spo2Percent}%` : 'Chưa đo'}
              </div>
            </div>
            <div className="rounded-xl bg-canvas border border-border p-2.5">
              <div className="text-[10px] font-semibold uppercase text-sub flex items-center gap-1">
                <Weight className="h-3 w-3 text-primary" aria-hidden="true" />
                <span>Cao / Nặng</span>
              </div>
              <div className="mt-1 font-mono text-xs font-extrabold text-navy">
                {heightWeightDisplay}
              </div>
            </div>
            <div className="rounded-xl bg-canvas border border-border p-2.5">
              <div className="text-[10px] font-semibold uppercase text-sub">Chỉ số BMI</div>
              <div className="mt-1 font-mono text-xs font-extrabold text-navy">
                {vitalSigns.bmi !== null ? `${vitalSigns.bmi} kg/m²` : 'Chưa tính'}
              </div>
            </div>
          </div>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-xs">
          <div className="rounded-xl bg-canvas border border-border p-3.5 space-y-1.5">
            <div className="font-bold text-navy flex items-center gap-1.5">
              <ClipboardList className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              <span>Lý do đến khám / Triệu chứng chính</span>
            </div>
            <p className="text-ink leading-relaxed">
              {clinicalContext.presentingComplaint ?? 'Chưa ghi nhận triệu chứng chính.'}
            </p>
          </div>

          <div className="rounded-xl bg-canvas border border-border p-3.5 space-y-1.5">
            <div className="font-bold text-navy flex items-center gap-1.5">
              <FileText className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              <span>Bệnh sử & Diễn tiến triệu chứng</span>
            </div>
            <p className="text-ink leading-relaxed">
              {clinicalContext.symptomHistory ?? 'Chưa ghi nhận bệnh sử chi tiết.'}
            </p>
          </div>

          <div className="rounded-xl bg-canvas border border-border p-3.5 space-y-1.5">
            <div className="font-bold text-navy">Khám lâm sàng tổng quát & Chuyên khoa</div>
            <p className="text-ink leading-relaxed">
              {clinicalContext.clinicalFindings ?? 'Chưa có ghi nhận khám thực thể.'}
            </p>
          </div>

          <div className="rounded-xl bg-canvas border border-border p-3.5 space-y-1.5">
            <div className="font-bold text-navy">Nhận định & Hướng xử trí ban đầu</div>
            <p className="text-ink leading-relaxed">
              {clinicalContext.clinicalConclusion ?? 'Chưa có kết luận lâm sàng.'}
            </p>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
