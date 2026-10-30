import { zodResolver } from '@hookform/resolvers/zod';
import { Info } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router-dom';

import { FormField, formControlClass } from '@/shared/components/form/form-primitives';
import { Button } from '@/shared/ui/button';

import { ServiceEditFooter, ServiceEditHeader } from '../components/service-edit-chrome';
import { DEPARTMENTS } from '../data/examination-services-data';
import {
  examinationServiceSchema,
  type ExaminationServiceFormValues,
} from '../schemas/examination-service.schema';
import { useExaminationServiceStore } from '../store/examination-service.store';

/** SCR-ADM-ESV-03 — edit dialog layered over the services list, matching DoctorEditModal architecture. */
export function ServiceEditModal() {
  const { serviceId } = useParams();
  const navigate = useNavigate();

  const service = useExaminationServiceStore((state) =>
    state.services.find((item) => item.id === serviceId || item.code === serviceId),
  );
  const updateService = useExaminationServiceStore((state) => state.updateService);
  const setPriceHistoryService = useExaminationServiceStore(
    (state) => state.setPriceHistoryService,
  );

  const [message, setMessage] = useState('');

  const {
    register,
    handleSubmit,
    setValue,
    control,
    reset,
    formState: { errors },
  } = useForm<ExaminationServiceFormValues>({
    resolver: zodResolver(examinationServiceSchema),
    defaultValues: service,
  });

  useEffect(() => {
    if (service) {
      reset(service);
    }
  }, [service, reset]);

  const status = useWatch({ control, name: 'status' });
  const onlineBooking = useWatch({ control, name: 'onlineBooking' }) ?? true;
  const description = useWatch({ control, name: 'description' }) ?? '';

  useEffect(() => {
    const handleKeys = (event: KeyboardEvent) => {
      if (event.key === 'Escape') navigate('/admin/services');
      if (event.ctrlKey && event.key.toLowerCase() === 's') {
        event.preventDefault();
        document.getElementById('service-edit-submit')?.click();
      }
    };
    window.addEventListener('keydown', handleKeys);
    return () => window.removeEventListener('keydown', handleKeys);
  }, [navigate]);

  if (!service) {
    return (
      <div className="fixed inset-0 z-50 grid place-items-center bg-slate-900/60 p-4">
        <div className="rounded-xl bg-white p-8 text-sm">
          <p>Không tìm thấy hồ sơ dịch vụ khám minh họa.</p>
          <Button className="mt-4" onClick={() => navigate('/admin/services')}>
            Quay lại danh sách
          </Button>
        </div>
      </div>
    );
  }

  const save = (values: ExaminationServiceFormValues) => {
    updateService(service.id, {
      name: values.name,
      category: values.category as ExaminationServiceFormValues['category'],
      department: values.department,
      description: values.description,
      onlineBooking: values.onlineBooking,
      status: values.status,
    });
    navigate('/admin/services');
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center overflow-y-auto bg-slate-900/60 p-3 backdrop-blur-[2px]"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) navigate('/admin/services');
      }}
    >
      <form
        role="dialog"
        aria-modal="true"
        aria-labelledby="service-edit-title"
        onSubmit={handleSubmit(save)}
        className="my-auto flex max-h-[calc(100vh-24px)] w-full max-w-[896px] flex-col overflow-hidden rounded-2xl border border-border bg-white shadow-2xl"
      >
        <ServiceEditHeader
          service={service}
          isActive={status === 'ACTIVE'}
          onClose={() => navigate('/admin/services')}
        />

        <div className="overflow-y-auto">
          {/* Operational notice banner */}
          <div className="mx-6 mt-5 flex items-start gap-2 rounded-lg border border-sky-200 bg-sky-50 px-4 py-3 text-[11px] leading-relaxed text-sky-900">
            <Info className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
            <p>
              <b>Lưu ý vận hành Khám Ngoại trú:</b> Cập nhật thông tin dịch vụ khám và cờ đặt lịch
              trực tuyến sẽ áp dụng ngay vào phiên làm việc hiện tại. Biểu phí được đồng bộ theo UC52;
              buồng khám và slot phân bổ theo UC53.
            </p>
          </div>

          {message && (
            <p
              role="status"
              className="mx-6 mt-3 rounded border border-amber-200 bg-amber-50 p-2 text-xs text-amber-700"
            >
              {message}
            </p>
          )}

          <div className="grid gap-x-5 gap-y-4 px-6 py-5 sm:grid-cols-2">
            {/* Service Code */}
            <FormField
              label="Mã Dịch vụ khám (Service ID)"
              required
              hint="Định danh chuẩn quốc tế và HIS (EX-x-xx)."
            >
              <input
                readOnly
                className={`${formControlClass} bg-slate-100 font-mono font-bold text-slate-700 cursor-not-allowed`}
                {...register('code')}
              />
            </FormField>

            {/* Department */}
            <FormField
              label="Khoa công tác trực thuộc / Chuyên môn quản lý"
              required
              error={errors.department?.message}
            >
              <select className={formControlClass} {...register('department')}>
                {DEPARTMENTS.map((item) => (
                  <option key={item}>{item}</option>
                ))}
              </select>
            </FormField>

            {/* Service Name */}
            <div className="sm:col-span-2">
              <FormField label="Tên Dịch vụ khám ngoại trú" required error={errors.name?.message}>
                <input
                  id="service-name-input"
                  className={formControlClass}
                  placeholder="Nhập tên dịch vụ khám ngoại trú..."
                  {...register('name')}
                />
              </FormField>
            </div>

            {/* Category */}
            <FormField label="Phân loại dịch vụ" required error={errors.category?.message}>
              <select className={formControlClass} {...register('category')}>
                <option value="Khám Lâm sàng">Khám Lâm sàng (EXAMINATION)</option>
                <option value="Hội chẩn Lâm sàng">Hội chẩn Lâm sàng (CONSULTATION)</option>
                <option value="Cận lâm sàng kỹ thuật">Cận lâm sàng kỹ thuật (DIAGNOSTIC)</option>
              </select>
            </FormField>

            {/* Price Reference (UC52) */}
            <FormField
              label="Đơn giá niêm yết hiện hành"
              hint="Chỉ xem tại đây. Biểu phí được phê duyệt theo quy trình UC52."
            >
              <div className="flex gap-2">
                <input
                  readOnly
                  className={`${formControlClass} bg-slate-100 font-bold text-slate-800`}
                  value={service.price.toLocaleString('vi-VN') + ' đ'}
                />
                <button
                  type="button"
                  className="mt-1.5 rounded border border-border px-2 text-[10px] text-sub hover:text-primary transition-colors cursor-pointer"
                  onClick={() => setPriceHistoryService(service)}
                  title="Xem lịch sử giá"
                >
                  Lịch sử
                </button>
              </div>
            </FormField>

            {/* Online Booking Switch Card */}
            <label className="flex items-center justify-between gap-2 rounded-lg border border-sky-200 bg-sky-50 px-3 py-2 text-[11px] sm:col-span-2 cursor-pointer">
              <span>
                <b>● Cho phép đặt lịch trực tuyến (Online Booking)</b>
                <small className="block text-sub">
                  Mở cho người bệnh đặt hẹn trực tuyến trên Cổng Patient Portal khi có slot khám
                </small>
              </span>
              <input
                type="checkbox"
                className="accent-primary h-4 w-4 rounded cursor-pointer"
                checked={onlineBooking}
                onChange={(event) =>
                  setValue('onlineBooking', event.target.checked, { shouldDirty: true })
                }
              />
            </label>

            {/* Description */}
            <div className="sm:col-span-2">
              <FormField
                label="Mô tả chi tiết phạm vi khám & Chỉ định lâm sàng"
                error={errors.description?.message}
              >
                <textarea
                  id="service-description-input"
                  rows={3}
                  maxLength={500}
                  className={formControlClass}
                  placeholder="Mô tả tóm tắt phạm vi khám, chỉ định cận lâm sàng..."
                  {...register('description')}
                />
              </FormField>
              <div className="mt-1 flex justify-between text-[10px] text-slate-400">
                <span>Nội dung hiển thị trên Cổng thông tin người bệnh và tiếp nhận.</span>
                <span>{description.length} / 500 ký tự</span>
              </div>
            </div>
          </div>
        </div>

        <ServiceEditFooter
          isActive={status === 'ACTIVE'}
          onClose={() => navigate('/admin/services')}
          onToggleStatus={() => {
            const next = status === 'ACTIVE' ? 'SUSPENDED' : 'ACTIVE';
            setValue('status', next, { shouldDirty: true });
            setMessage(
              next === 'SUSPENDED'
                ? 'Dịch vụ đã chuyển sang trạng thái Tạm dừng (SUSPENDED).'
                : 'Dịch vụ đã chuyển sang trạng thái Hoạt động (ACTIVE).',
            );
          }}
        />
      </form>
    </div>
  );
}

export { ServiceEditModal as ExaminationServiceEditModal };
