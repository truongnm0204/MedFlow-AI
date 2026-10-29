import { CheckCircle2 } from 'lucide-react';
import type { FieldErrors, UseFormRegister } from 'react-hook-form';

import {
  FormField,
  FormSectionCard,
  formControlClass,
} from '@/shared/components/form/form-primitives';

import { DEPARTMENTS } from '../data/examination-services-data';
import type { ExaminationServiceFormValues } from '../schemas/examination-service.schema';

interface ServiceCreateFormProps {
  register: UseFormRegister<ExaminationServiceFormValues>;
  errors: FieldErrors<ExaminationServiceFormValues>;
  category: string;
  onCategoryChange: (cat: 'Khám Lâm sàng' | 'Cận lâm sàng kỹ thuật') => void;
  onlineBooking: boolean;
  onOnlineBookingChange: (val: boolean) => void;
  descriptionLength: number;
}

export function ServiceCreateForm({
  register,
  errors,
  category,
  onCategoryChange,
  onlineBooking,
  onOnlineBookingChange,
  descriptionLength,
}: ServiceCreateFormProps) {
  return (
    <div className="space-y-6">
      {/* CARD 1: IDENTITY & DEPARTMENT */}
      <FormSectionCard
        stepNumber="1"
        title="Định danh dịch vụ & chuyên khoa quản lý"
        note="Bắt buộc (*)"
      >
        <div className="space-y-5">
          {/* Service Code */}
          <FormField
            label="Mã Dịch vụ"
            required
            topNote="Đề xuất tự động theo chuẩn HIS (EX-x-xx)"
            error={errors.code?.message}
            hint={
              <span className="flex items-center gap-1.5 text-sky-700 font-medium">
                <span className="text-sky-600">🛡️</span>
                Mã duy nhất, hợp lệ, chưa từng được gán cho dịch vụ khám khác.
              </span>
            }
          >
            <div className="relative mt-1.5 flex items-center">
              <span className="pointer-events-none absolute left-3 flex items-center gap-0.5 text-sky-600 font-mono font-bold tracking-tighter">
                <span className="inline-block h-3.5 w-0.5 bg-sky-500 rounded-xs" />
                <span className="inline-block h-3.5 w-1 bg-sky-500 rounded-xs" />
                <span className="inline-block h-3.5 w-0.5 bg-sky-500 rounded-xs" />
                <span className="inline-block h-3.5 w-1.5 bg-sky-500 rounded-xs" />
              </span>
              <input
                id="service-code-input"
                {...register('code')}
                className={`${formControlClass} !mt-0 pl-10 pr-10 font-medium`}
                placeholder="EX-2-09"
              />
              <span className="pointer-events-none absolute right-3 flex items-center text-sky-600">
                <CheckCircle2 className="h-4 w-4 text-sky-600" />
              </span>
            </div>
          </FormField>

          {/* Service Name */}
          <FormField
            label="Tên Dịch vụ khám"
            required
            error={errors.name?.message}
            hint="Tên dịch vụ hiển thị trên cổng đặt lịch và các bước tiếp nhận."
          >
            <input
              id="service-name-input"
              {...register('name')}
              className={formControlClass}
              placeholder="Khám Chuyên khoa Nội Tiêu hóa & Gan Mật Chuyên sâu"
            />
          </FormField>

          {/* Category Radio Cards */}
          <FormField label="Phân loại dịch vụ" required error={errors.category?.message}>
            <div className="mt-2 grid grid-cols-1 gap-3 sm:grid-cols-2">
              <button
                type="button"
                onClick={() => onCategoryChange('Khám Lâm sàng')}
                className={`flex items-center justify-center gap-2 rounded-lg border p-3 text-xs font-semibold transition-all shadow-xs ${
                  category === 'Khám Lâm sàng'
                    ? 'border-primary bg-sky-50/60 text-primary ring-1 ring-primary'
                    : 'border-slate-200 bg-white text-slate-700 hover:border-slate-300'
                }`}
              >
                <span>🩺</span>
                <span>Khám Lâm sàng (EXAMINATION)</span>
              </button>

              <button
                type="button"
                onClick={() => onCategoryChange('Cận lâm sàng kỹ thuật')}
                className={`flex items-center justify-center gap-2 rounded-lg border p-3 text-xs font-semibold transition-all shadow-xs ${
                  category === 'Cận lâm sàng kỹ thuật'
                    ? 'border-primary bg-sky-50/60 text-primary ring-1 ring-primary'
                    : 'border-slate-200 bg-white text-slate-700 hover:border-slate-300'
                }`}
              >
                <span>🔬</span>
                <span>Cận lâm sàng kỹ thuật (DIAGNOSTIC)</span>
              </button>
            </div>
          </FormField>

          {/* Department Select */}
          <FormField label="Khoa chuyên môn quản lý" required error={errors.department?.message}>
            <select
              id="service-department-select"
              {...register('department')}
              className={`${formControlClass} cursor-pointer`}
            >
              <option value="">-- Chọn khoa phòng quản lý --</option>
              {DEPARTMENTS.map((dept) => (
                <option key={dept} value={dept}>
                  {dept}
                </option>
              ))}
            </select>
          </FormField>

          {/* Detailed Description */}
          <FormField
            label="Mô tả chi tiết phạm vi khám"
            topNote={`Tối đa 500 ký tự (${descriptionLength}/500)`}
            error={errors.description?.message}
          >
            <textarea
              id="service-description-input"
              {...register('description')}
              rows={3}
              maxLength={500}
              className={`${formControlClass} resize-y leading-relaxed`}
              placeholder="Thăm khám, chẩn đoán phân biệt và tư vấn phác đồ điều trị chuyên sâu..."
            />
          </FormField>
        </div>
      </FormSectionCard>

      {/* CARD 2: BOOKING CONFIGURATION */}
      <FormSectionCard stepNumber="2" title="Cấu hình đặt lịch" note="Thiết lập cổng đặt lịch">
        <FormField
          label="Cho phép đặt lịch trực tuyến"
          required
          topNote="Chọn Có hoặc Không"
          hint="Khi bật, người bệnh có thể chọn slot còn chỗ đã khai báo tại UC53."
        >
          <select
            id="service-online-booking-select"
            value={onlineBooking ? 'true' : 'false'}
            onChange={(e) => onOnlineBookingChange(e.target.value === 'true')}
            className={`${formControlClass} cursor-pointer`}
          >
            <option value="true">Có</option>
            <option value="false">Không</option>
          </select>
        </FormField>
      </FormSectionCard>
    </div>
  );
}
