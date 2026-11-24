import { zodResolver } from '@hookform/resolvers/zod';
import { Clock, Info } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { Link, useNavigate } from 'react-router-dom';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

import { ServiceCreateActionBar } from '../components/service-create-action-bar';
import { ServiceCreateForm } from '../components/service-create-form';
import { ServiceCreateSidebar } from '../components/service-create-sidebar';
import { DEPARTMENTS } from '../data/examination-services-data';
import { useCreateExaminationServiceMutation } from '../hooks/use-examination-services';
import {
  examinationServiceSchema,
  type ExaminationServiceFormValues,
} from '../schemas/examination-service.schema';
import { useExaminationServiceStore } from '../store/examination-service.store';

const EMPTY_SERVICE_FORM_VALUES: ExaminationServiceFormValues = {
  code: '',
  name: '',
  category: 'Khám Lâm sàng',
  department: DEPARTMENTS[0],
  description: '',
  onlineBooking: true,
  status: 'ACTIVE',
};

export function ExaminationServiceCreatePage() {
  const navigate = useNavigate();
  const draft = useExaminationServiceStore((state) => state.draft);
  const saveDraft = useExaminationServiceStore((state) => state.saveDraft);
  const clearDraft = useExaminationServiceStore((state) => state.clearDraft);
  const createServiceMutation = useCreateExaminationServiceMutation();

  const [message, setMessage] = useState<string | null>(null);
  const [isDraftSaved, setIsDraftSaved] = useState(false);

  const {
    register,
    handleSubmit,
    setValue,
    getValues,
    control,
    formState: { errors, isDirty },
  } = useForm<ExaminationServiceFormValues>({
    resolver: zodResolver(examinationServiceSchema),
    defaultValues: {
      code: draft?.code ?? EMPTY_SERVICE_FORM_VALUES.code,
      name: draft?.name ?? EMPTY_SERVICE_FORM_VALUES.name,
      category: draft?.category ?? EMPTY_SERVICE_FORM_VALUES.category,
      department: draft?.department ?? EMPTY_SERVICE_FORM_VALUES.department,
      description: draft?.description ?? EMPTY_SERVICE_FORM_VALUES.description,
      onlineBooking: draft?.onlineBooking ?? EMPTY_SERVICE_FORM_VALUES.onlineBooking,
      status: draft?.status ?? EMPTY_SERVICE_FORM_VALUES.status,
    },
  });

  const category = useWatch({ control, name: 'category' }) ?? 'Khám Lâm sàng';
  const onlineBooking = useWatch({ control, name: 'onlineBooking' }) ?? true;
  const description = useWatch({ control, name: 'description' }) ?? '';

  useEffect(() => {
    if (!isDirty) return;
    const timer = window.setInterval(() => {
      saveDraft(getValues());
      setIsDraftSaved(true);
    }, 30_000);
    return () => window.clearInterval(timer);
  }, [getValues, isDirty, saveDraft]);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') navigate('/admin/services');
      if (e.ctrlKey && e.key.toLowerCase() === 's') {
        e.preventDefault();
        document.getElementById('service-create-submit')?.click();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [navigate]);

  const handleManualDraftSave = () => {
    saveDraft(getValues());
    setIsDraftSaved(true);
    setMessage('Đã lưu bản nháp vào bộ nhớ tạm của phiên.');
    setTimeout(() => setMessage(null), 3000);
  };

  const handleCreate = async (values: ExaminationServiceFormValues) => {
    setMessage(null);
    try {
      await createServiceMutation.mutateAsync(values);
      clearDraft();
      navigate('/admin/services');
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Không thể tạo mới dịch vụ khám.');
    }
  };

  return (
    <AdminPortalShell
      showTabs={false}
      breadcrumbs={[
        { label: 'Cấu hình Cơ sở' },
        { label: 'Danh mục Dịch vụ Khám', href: '/admin/services' },
        { label: 'Thêm mới Dịch vụ Khám (SCR-ADM-ESV-02)' },
      ]}
    >
      <form onSubmit={handleSubmit(handleCreate)} noValidate>
        <main className="px-6 pb-28 pt-6">
          <div className="flex flex-wrap items-center justify-between gap-3 text-xs">
            <div className="flex flex-wrap items-center gap-3">
              <Link
                to="/admin/services"
                className="rounded-lg border border-border bg-white px-3 py-1.5 text-xs text-sub shadow-xs hover:border-slate-300 hover:text-primary transition-colors"
              >
                ← Quay lại Danh mục Dịch vụ (SCR-ADM-ESV-01)
              </Link>
              <span className="text-slate-300">|</span>
              <span className="rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-[11px] font-semibold text-primary">
                ● Tạo mới (Draft Mode)
              </span>
              <span className="text-slate-500 text-[11px]">Mã màn hình: SCR-ADM-ESV-02</span>
            </div>

            <div className="flex items-center gap-2 text-xs text-slate-500">
              <Clock className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
              <span>{isDraftSaved ? 'Đã lưu bản nháp' : 'Thay đổi chưa lưu'}</span>
            </div>
          </div>

          <div className="mt-4">
            <h1 className="text-2xl font-bold tracking-tight text-ink">
              Thêm mới Dịch vụ Khám Ngoại trú
            </h1>
            <p className="mt-1 text-xs text-slate-500">
              Khai báo mã, tên, loại và khoa quản lý. Bật đặt lịch trực tuyến khi dịch vụ còn slot
              hợp lệ.
            </p>
          </div>

          <div className="my-5 flex items-start gap-3 rounded-lg border border-sky-200 bg-sky-50/70 p-4 text-xs leading-relaxed text-sky-900 shadow-xs">
            <span className="grid h-6 w-6 shrink-0 place-items-center rounded-full bg-white text-sky-600 shadow-2xs mt-0.5">
              <Info className="h-4 w-4" aria-hidden="true" />
            </span>
            <p>
              <span className="font-bold">Lưu ý vận hành Khám Ngoại trú:</span> Dịch vụ lưu thông
              tin danh mục và cờ đặt lịch trực tuyến. Thời lượng, bác sĩ và phòng được gán theo slot
              tại UC53; biểu phí áp dụng theo bảng giá cơ sở ở UC52 (không tự động gán giá mặc
              định).
            </p>
          </div>

          {message && (
            <div
              role="status"
              className="mb-5 rounded-lg border border-amber-200 bg-amber-50 p-3 text-xs text-amber-800 shadow-xs"
            >
              {message}
            </div>
          )}

          <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1.65fr)_minmax(340px,1fr)]">
            <ServiceCreateForm
              register={register}
              errors={errors}
              category={category}
              onCategoryChange={(val) => setValue('category', val, { shouldDirty: true })}
              onlineBooking={onlineBooking}
              onOnlineBookingChange={(val) => setValue('onlineBooking', val, { shouldDirty: true })}
              descriptionLength={description.length}
            />

            <ServiceCreateSidebar />
          </div>
        </main>

        <ServiceCreateActionBar
          onCancel={() => navigate('/admin/services')}
          onSaveDraft={handleManualDraftSave}
        />
      </form>
    </AdminPortalShell>
  );
}
