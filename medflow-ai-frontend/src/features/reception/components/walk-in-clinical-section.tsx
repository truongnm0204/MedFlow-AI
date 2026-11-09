import { useState } from 'react';
import { Activity, Building2, FileText, Stethoscope } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type {
  ClinicalPriority,
  WalkInDepartment,
  WalkInService,
} from '../types/walk-in-visit.types';

interface WalkInClinicalSectionProps {
  priority: ClinicalPriority;
  onPriorityChange: (priority: ClinicalPriority) => void;
  departments: WalkInDepartment[];
  selectedDepartmentId: string;
  onDepartmentChange: (departmentId: string) => void;
  selectedServiceId: string;
  onServiceChange: (serviceId: string) => void;
  currentService: WalkInService | null;
  reasonForVisit: string;
  onReasonChange: (reason: string) => void;
  symptoms: string[];
  onToggleSymptom: (symptom: string) => void;
  symptomOptions: string[];
  errors: Record<string, string>;
}

export function WalkInClinicalSection({
  priority,
  onPriorityChange,
  departments,
  selectedDepartmentId,
  onDepartmentChange,
  selectedServiceId,
  onServiceChange,
  currentService,
  reasonForVisit,
  onReasonChange,
  symptoms,
  onToggleSymptom,
  symptomOptions,
  errors,
}: WalkInClinicalSectionProps) {
  const [receptionTimeLabel] = useState(() => {
    const now = new Date();
    return `${now.toLocaleDateString('vi-VN')} - ${now.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}`;
  });

  const priorities: Array<{
    id: ClinicalPriority;
    label: string;
    desc: string;
    tone: string;
  }> = [
    {
      id: 'STANDARD',
      label: 'Khám thông thường (Standard Outpatient)',
      desc: 'Xếp slot tiêu chuẩn theo hàng đợi',
      tone: 'border-sky-300 bg-sky-50/60 text-sky-900',
    },
    {
      id: 'PRIORITY',
      label: 'Ưu tiên người cao tuổi / Thai sản / Trẻ nhỏ',
      desc: 'Cấp số ưu tiên xen kẽ hàng đợi',
      tone: 'border-amber-300 bg-amber-50/60 text-amber-900',
    },
    {
      id: 'EMERGENCY',
      label: 'Cấp cứu / Chuyển khoa khẩn',
      desc: 'Chuyển thẳng buồng cấp cứu xử trí ngay',
      tone: 'border-rose-300 bg-rose-50/60 text-rose-900',
    },
  ];

  const selectedDepartment = departments.find((d) => d.id === selectedDepartmentId);
  const availableServices = selectedDepartment ? selectedDepartment.services : [];

  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-6 shadow-2xs space-y-6">
      <div className="border-b border-slate-100 pb-4">
        <h2 className="text-base font-bold text-slate-900 tracking-tight flex items-center gap-2">
          <Stethoscope className="w-5 h-5 text-primary" aria-hidden="true" />
          <span>Thông tin ca khám vãng lai & Triệu chứng ban đầu</span>
        </h2>
        <p className="text-xs text-slate-500 mt-0.5">
          Tiếp nhận bệnh nhân đến trực tiếp tại quầy chưa có lịch hẹn trực tuyến
        </p>
      </div>

      {/* Meta context info */}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 text-xs bg-slate-50 p-4 rounded-xl border border-slate-200/80">
        <div>
          <span className="text-slate-500">Thời gian tiếp nhận thực tế:</span>
          <div className="font-bold text-slate-800 font-mono mt-0.5">{receptionTimeLabel}</div>
        </div>
        <div>
          <span className="text-slate-500">Loại hình tiếp nhận:</span>
          <div className="font-bold text-rose-700 mt-0.5">
            ● Vãng lai trực tiếp tại quầy (Không có lịch hẹn trước)
          </div>
        </div>
      </div>

      {/* Priority Selector */}
      <div className="space-y-2">
        <label className="text-xs font-bold text-slate-800 block">
          Mức độ ưu tiên lâm sàng (Clinical Priority) <span className="text-rose-500">*</span>
        </label>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
          {priorities.map((item) => {
            const isSelected = priority === item.id;
            return (
              <button
                key={item.id}
                type="button"
                onClick={() => onPriorityChange(item.id)}
                className={`p-3.5 rounded-xl border text-left transition-all ${
                  isSelected
                    ? `${item.tone} ring-2 ring-primary shadow-xs font-semibold`
                    : 'border-slate-200 bg-white hover:border-slate-300 text-slate-700'
                }`}
              >
                <div className="text-xs font-bold">{item.label}</div>
                <div className="text-[11px] opacity-75 mt-0.5">{item.desc}</div>
              </button>
            );
          })}
        </div>
      </div>

      {/* Department & Service Selection */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-5 pt-2 border-t border-slate-100">
        {/* Department Select */}
        <div className="space-y-1.5">
          <label
            htmlFor="departmentSelect"
            className="text-xs font-bold text-slate-800 flex items-center gap-1.5"
          >
            <Building2 className="w-3.5 h-3.5 text-primary" aria-hidden="true" />
            <span>
              Khoa khám tiếp nhận <span className="text-rose-500">*</span>
            </span>
          </label>
          <select
            id="departmentSelect"
            value={selectedDepartmentId}
            onChange={(e) => onDepartmentChange(e.target.value)}
            className={`w-full h-11 px-3.5 rounded-xl border bg-white text-xs text-slate-900 focus:outline-hidden focus:ring-2 focus:ring-primary ${
              errors.departmentId ? 'border-rose-400' : 'border-slate-200'
            }`}
          >
            <option value="">-- Chọn khoa phòng tiếp nhận --</option>
            {departments.map((dept) => (
              <option key={dept.id} value={dept.id}>
                {dept.name} ({dept.location})
              </option>
            ))}
          </select>
          {errors.departmentId && (
            <p className="text-[11px] text-rose-600 font-medium">{errors.departmentId}</p>
          )}
        </div>

        {/* Service Select */}
        <div className="space-y-1.5">
          <div className="flex items-center justify-between">
            <label
              htmlFor="serviceSelect"
              className="text-xs font-bold text-slate-800 flex items-center gap-1.5"
            >
              <Activity className="w-3.5 h-3.5 text-emerald-600" aria-hidden="true" />
              <span>
                Dịch vụ khám yêu cầu <span className="text-rose-500">*</span>
              </span>
            </label>
            {currentService && (
              <Badge tone="sky">
                <span className="font-bold text-xs font-mono">{currentService.priceFormatted}</span>
              </Badge>
            )}
          </div>
          <select
            id="serviceSelect"
            value={selectedServiceId}
            onChange={(e) => onServiceChange(e.target.value)}
            disabled={!selectedDepartmentId}
            className={`w-full h-11 px-3.5 rounded-xl border bg-white text-xs text-slate-900 focus:outline-hidden focus:ring-2 focus:ring-primary disabled:bg-slate-100 disabled:text-slate-400 ${
              errors.serviceId ? 'border-rose-400' : 'border-slate-200'
            }`}
          >
            <option value="">-- Chọn dịch vụ khám chuyên khoa --</option>
            {availableServices.map((srv) => (
              <option key={srv.id} value={srv.id}>
                {srv.name} ({srv.code}) - {srv.priceFormatted}
              </option>
            ))}
          </select>
          {errors.serviceId && (
            <p className="text-[11px] text-rose-600 font-medium">{errors.serviceId}</p>
          )}
        </div>
      </div>

      {/* Reason for visit & Clinical symptoms */}
      <div className="space-y-3 pt-2 border-t border-slate-100">
        <div className="space-y-1.5">
          <label
            htmlFor="reasonForVisit"
            className="text-xs font-bold text-slate-800 flex items-center gap-1.5"
          >
            <FileText className="w-3.5 h-3.5 text-primary" aria-hidden="true" />
            <span>
              Lý do đến khám / Triệu chứng lâm sàng sơ bộ <span className="text-rose-500">*</span>
            </span>
          </label>
          <textarea
            id="reasonForVisit"
            rows={3}
            value={reasonForVisit}
            onChange={(e) => onReasonChange(e.target.value)}
            placeholder="Ví dụ: Bệnh nhân đau quặn vùng thượng vị sau ăn khoảng 1-2 tiếng, ợ chua nhiều kèm cồn cào buồn nôn 2 ngày nay..."
            className={`w-full p-3.5 rounded-xl border bg-white text-xs text-slate-900 placeholder:text-slate-400 focus:outline-hidden focus:ring-2 focus:ring-primary ${
              errors.reasonForVisit ? 'border-rose-400' : 'border-slate-200'
            }`}
          />
          {errors.reasonForVisit && (
            <p className="text-[11px] text-rose-600 font-medium">{errors.reasonForVisit}</p>
          )}
        </div>

        {/* Symptoms pills */}
        <div className="space-y-1.5">
          <label className="text-[11px] font-semibold text-slate-600 block">
            Chọn nhanh các triệu chứng ghi nhận được:
          </label>
          <div className="flex flex-wrap gap-2">
            {symptomOptions.map((sym) => {
              const isChecked = symptoms.includes(sym);
              return (
                <button
                  key={sym}
                  type="button"
                  onClick={() => onToggleSymptom(sym)}
                  className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-colors ${
                    isChecked
                      ? 'bg-primary text-white shadow-2xs font-semibold'
                      : 'bg-slate-100 text-slate-700 hover:bg-slate-200 border border-slate-200/80'
                  }`}
                >
                  {sym} {isChecked && '✓'}
                </button>
              );
            })}
          </div>
        </div>
      </div>
    </div>
  );
}
