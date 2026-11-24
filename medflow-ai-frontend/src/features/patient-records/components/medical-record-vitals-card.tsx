import { Activity, HeartPulse, Ruler, Scale, Thermometer, Wind } from 'lucide-react';

import type { PatientMedicalRecordVitalSigns } from '../types/patient-records.types';

export interface MedicalRecordVitalsCardProps {
  vitalSigns: PatientMedicalRecordVitalSigns | null;
}

export function MedicalRecordVitalsCard({ vitalSigns }: MedicalRecordVitalsCardProps) {
  const items = vitalSigns
    ? [
        {
          label: 'Huyết áp',
          value: vitalSigns.bloodPressureLabel ?? '—',
          icon: HeartPulse,
        },
        {
          label: 'Mạch',
          value: vitalSigns.pulseBpm != null ? `${vitalSigns.pulseBpm} lần/phút` : '—',
          icon: Activity,
        },
        {
          label: 'Nhiệt độ',
          value: vitalSigns.temperatureC != null ? `${vitalSigns.temperatureC} °C` : '—',
          icon: Thermometer,
        },
        {
          label: 'SpO₂',
          value: vitalSigns.spo2Percent != null ? `${vitalSigns.spo2Percent}%` : '—',
          icon: Wind,
        },
        {
          label: 'Chiều cao / Cân nặng',
          value:
            vitalSigns.heightCm != null || vitalSigns.weightKg != null
              ? `${vitalSigns.heightCm ?? '—'} cm • ${vitalSigns.weightKg ?? '—'} kg`
              : '—',
          icon: Ruler,
        },
        {
          label: 'Chỉ số BMI',
          value: vitalSigns.bmi != null ? String(vitalSigns.bmi) : '—',
          icon: Scale,
        },
      ]
    : [];

  return (
    <section className="rounded-2xl border border-border bg-white p-5 shadow-xs">
      <h2 className="text-sm font-bold text-navy">Sinh hiệu lâm sàng</h2>
      {!vitalSigns ? (
        <p className="mt-2 text-xs text-sub">
          Chưa ghi nhận chỉ số sinh hiệu chi tiết trong lượt khám này.
        </p>
      ) : (
        <dl className="mt-3.5 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
          {items.map(({ label, value, icon: Icon }) => (
            <div key={label} className="rounded-xl border border-border bg-canvas p-3">
              <dt className="flex items-center gap-1.5 text-[11px] font-medium text-sub">
                <Icon className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                <span>{label}</span>
              </dt>
              <dd className="mt-1.5 font-mono text-xs font-bold text-ink">{value}</dd>
            </div>
          ))}
        </dl>
      )}
    </section>
  );
}
