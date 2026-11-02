import { Activity, AlertTriangle, FileText } from 'lucide-react';

import type { CheckInFormData, CheckInPriority } from '../types/checkin.types';

interface CheckInVitalsSectionProps {
  formData: CheckInFormData;
  onChange: (data: Partial<CheckInFormData>) => void;
  errors?: Record<string, string>;
}

export function CheckInVitalsSection({
  formData,
  onChange,
  errors = {},
}: CheckInVitalsSectionProps) {
  const priorities: Array<{ id: CheckInPriority; label: string; tone: string }> = [
    { id: 'NORMAL', label: 'Bình thường', tone: 'text-slate-700 bg-slate-50' },
    { id: 'ELDERLY', label: 'Người cao tuổi', tone: 'text-amber-700 bg-amber-50' },
    { id: 'PREGNANT', label: 'Phụ nữ có thai', tone: 'text-sky-700 bg-sky-50' },
    { id: 'URGENT', label: 'Ưu tiên cấp cứu', tone: 'text-rose-700 bg-rose-50' },
  ];

  return (
    <div className="space-y-4 text-xs">
      {/* Reason for visit */}
      <div className="space-y-1.5">
        <label
          htmlFor="reasonForVisit"
          className="font-bold text-slate-800 flex items-center gap-1.5"
        >
          <FileText className="w-3.5 h-3.5 text-primary" aria-hidden="true" />
          <span>Lý do đến khám hôm nay <span className="text-rose-500">*</span></span>
        </label>
        <textarea
          id="reasonForVisit"
          rows={2}
          value={formData.reasonForVisit}
          onChange={(e) => onChange({ reasonForVisit: e.target.value })}
          placeholder="Ví dụ: Tái khám cao huyết áp định kỳ, ho khan kèm rát họng 3 ngày nay..."
          className={`w-full p-3 rounded-xl border bg-white text-slate-900 placeholder:text-slate-400 focus:outline-hidden focus:ring-2 focus:ring-primary ${
            errors.reasonForVisit ? 'border-rose-400 focus:ring-rose-400' : 'border-slate-200'
          }`}
        />
        {errors.reasonForVisit && (
          <p className="text-[11px] text-rose-600 font-medium">{errors.reasonForVisit}</p>
        )}
      </div>

      {/* Vitals inputs */}
      <div className="space-y-2">
        <div className="font-bold text-slate-800 flex items-center gap-1.5">
          <Activity className="w-3.5 h-3.5 text-emerald-600" aria-hidden="true" />
          <span>Chỉ số sinh hiệu sàng lọc ban đầu (nếu đo tại quầy)</span>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
          {/* Blood pressure */}
          <div className="space-y-1">
            <label htmlFor="bloodPressure" className="text-[11px] text-slate-500 font-medium">
              Huyết áp (mmHg)
            </label>
            <input
              id="bloodPressure"
              type="text"
              placeholder="120/80"
              value={formData.bloodPressure || ''}
              onChange={(e) => onChange({ bloodPressure: e.target.value })}
              className={`w-full h-9 px-3 rounded-lg border bg-white font-mono text-xs ${
                errors.bloodPressure ? 'border-rose-400' : 'border-slate-200'
              }`}
            />
            {errors.bloodPressure && (
              <p className="text-[10px] text-rose-600">{errors.bloodPressure}</p>
            )}
          </div>

          {/* Heart rate */}
          <div className="space-y-1">
            <label htmlFor="heartRate" className="text-[11px] text-slate-500 font-medium">
              Nhịp tim (bpm)
            </label>
            <input
              id="heartRate"
              type="number"
              placeholder="75"
              value={formData.heartRate || ''}
              onChange={(e) =>
                onChange({ heartRate: e.target.value ? Number(e.target.value) : undefined })
              }
              className={`w-full h-9 px-3 rounded-lg border bg-white font-mono text-xs ${
                errors.heartRate ? 'border-rose-400' : 'border-slate-200'
              }`}
            />
            {errors.heartRate && (
              <p className="text-[10px] text-rose-600">{errors.heartRate}</p>
            )}
          </div>

          {/* Temperature */}
          <div className="space-y-1">
            <label htmlFor="temperature" className="text-[11px] text-slate-500 font-medium">
              Thân nhiệt (°C)
            </label>
            <input
              id="temperature"
              type="number"
              step="0.1"
              placeholder="36.5"
              value={formData.temperature || ''}
              onChange={(e) =>
                onChange({ temperature: e.target.value ? Number(e.target.value) : undefined })
              }
              className={`w-full h-9 px-3 rounded-lg border bg-white font-mono text-xs ${
                errors.temperature ? 'border-rose-400' : 'border-slate-200'
              }`}
            />
            {errors.temperature && (
              <p className="text-[10px] text-rose-600">{errors.temperature}</p>
            )}
          </div>
        </div>
      </div>

      {/* Priority Selection */}
      <div className="space-y-1.5">
        <label className="font-bold text-slate-800">Mức độ ưu tiên tiếp nhận</label>
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
          {priorities.map((item) => (
            <button
              key={item.id}
              type="button"
              onClick={() => onChange({ priority: item.id })}
              className={`p-2 rounded-xl border text-center transition-all ${
                formData.priority === item.id
                  ? 'border-primary bg-primary/5 text-primary font-bold shadow-2xs'
                  : 'border-slate-200 hover:border-slate-300 text-slate-600'
              }`}
            >
              <div className="text-[11px] truncate">{item.label}</div>
            </button>
          ))}
        </div>
      </div>

      {/* Verification Checkboxes */}
      <div className="p-3.5 rounded-xl bg-slate-50 border border-slate-200 space-y-2.5">
        <label className="flex items-start gap-2.5 cursor-pointer">
          <input
            type="checkbox"
            checked={formData.isIdentityVerified}
            onChange={(e) => onChange({ isIdentityVerified: e.target.checked })}
            className="w-4 h-4 rounded text-primary border-slate-300 focus:ring-primary mt-0.5"
          />
          <div className="text-xs">
            <span className="font-bold text-slate-900">
              Đã đối chiếu thông tin CCCD / VNeID trùng khớp <span className="text-rose-500">*</span>
            </span>
            <p className="text-[11px] text-slate-500">
              Xác nhận bệnh nhân có mặt trực tiếp tại quầy và khớp thông tin với vé hẹn.
            </p>
          </div>
        </label>
        {errors.isIdentityVerified && (
          <p className="text-[11px] text-rose-600 font-semibold pl-6 flex items-center gap-1">
            <AlertTriangle className="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
            {errors.isIdentityVerified}
          </p>
        )}

        <label className="flex items-start gap-2.5 cursor-pointer pt-1 border-t border-slate-200/60">
          <input
            type="checkbox"
            checked={formData.isInsuranceVerified}
            onChange={(e) => onChange({ isInsuranceVerified: e.target.checked })}
            className="w-4 h-4 rounded text-primary border-slate-300 focus:ring-primary mt-0.5"
          />
          <div className="text-xs">
            <span className="font-semibold text-slate-800">
              Đã kiểm tra tính hợp lệ của thẻ BHYT trên cổng giám định
            </span>
            <p className="text-[11px] text-slate-500">
              (Bỏ qua nếu bệnh nhân khám dịch vụ tự nguyện).
            </p>
          </div>
        </label>
      </div>
    </div>
  );
}
