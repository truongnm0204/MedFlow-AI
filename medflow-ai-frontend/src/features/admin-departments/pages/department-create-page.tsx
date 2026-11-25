import { zodResolver } from '@hookform/resolvers/zod';
import { Building2 } from 'lucide-react';
import { useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { Link, useNavigate } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { UnsavedChangesModal, useUnsavedChanges } from '@/shared/hooks/use-unsaved-changes';
import { loadAdminDraft, removeAdminDraft, saveAdminDraft } from '@/shared/lib/admin-draft-storage';

import { useAdminHospitalOptions, useCreateDepartmentMutation } from '../hooks/use-departments';
import {
  departmentCreateSchema,
  type DepartmentCreateFormValues,
} from '../schemas/department-create.schema';
import type { CreateDepartmentInput } from '../types/department-create.types';

const STATUS_LABELS: Record<CreateDepartmentInput['status'], string> = {
  ACTIVE: 'Đang hoạt động',
  INACTIVE: 'Tạm ngừng',
};

const LIST_PATH = '/admin/departments';

const inputClass =
  'w-full rounded-lg border border-border bg-white px-3 py-2 text-sm text-ink outline-none focus:border-primary focus:ring-2 focus:ring-primary/20';

export function DepartmentCreatePage() {
  const navigate = useNavigate();
  const user = useAuthStore((state) => state.user);
  const [message, setMessage] = useState<string | null>(null);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  const hospitalsQuery = useAdminHospitalOptions();
  const createMutation = useCreateDepartmentMutation();
  const facilities = hospitalsQuery.data ?? [];

  const [initialDraft] = useState<DepartmentCreateFormValues>(() => {
    const stored = loadAdminDraft<Partial<DepartmentCreateFormValues>>('department-create', {
      userId: user?.id ?? null,
    });
    const parsed = stored ? departmentCreateSchema.partial().safeParse(stored) : null;
    return {
      code: parsed?.success && parsed.data.code ? parsed.data.code : '',
      name: parsed?.success && parsed.data.name ? parsed.data.name : '',
      facilityId: parsed?.success && parsed.data.facilityId ? parsed.data.facilityId : '',
      status: parsed?.success && parsed.data.status ? parsed.data.status : 'ACTIVE',
    };
  });

  const {
    register,
    handleSubmit,
    getValues,
    reset,
    control,
    formState: { errors, isDirty },
  } = useForm<DepartmentCreateFormValues>({
    resolver: zodResolver(departmentCreateSchema),
    defaultValues: initialDraft,
  });

  const { isBlocked, confirmNavigation, cancelNavigation, allowNavigation, guardAction } =
    useUnsavedChanges(isDirty);

  const code = useWatch({ control, name: 'code' });
  const name = useWatch({ control, name: 'name' });
  const facilityId = useWatch({ control, name: 'facilityId' });
  const status = useWatch({ control, name: 'status' });

  const facilityName = facilities.find((f) => String(f.id) === facilityId)?.name;

  const handleCreate = async (values: DepartmentCreateFormValues) => {
    setErrorMsg(null);
    const rawSnapshot = structuredClone(getValues());
    try {
      const parsedHospitalId = /^\d+$/.test(values.facilityId) ? Number(values.facilityId) : null;
      await createMutation.mutateAsync({
        code: values.code.trim(),
        name: values.name.trim(),
        hospitalId: parsedHospitalId,
        isActive: values.status === 'ACTIVE',
      });
      const editedDuringSubmit = JSON.stringify(getValues()) !== JSON.stringify(rawSnapshot);
      removeAdminDraft('department-create', { userId: user?.id ?? null });
      reset(rawSnapshot, { keepValues: true });
      if (!editedDuringSubmit) {
        allowNavigation();
        navigate(LIST_PATH);
      } else {
        guardAction(() => navigate(LIST_PATH));
      }
    } catch (error) {
      setErrorMsg(error instanceof Error ? error.message : 'Không thể khai báo khoa phòng mới.');
    }
  };

  const handleSaveDraft = () => {
    const snapshot: DepartmentCreateFormValues = { ...getValues() };
    const result = saveAdminDraft('department-create', snapshot, {
      userId: user?.id ?? null,
    });
    if (!result.ok) {
      setMessage(null);
      setErrorMsg(result.error);
      return;
    }
    setErrorMsg(null);
    reset(snapshot, { keepValues: true });
    setMessage(`Đã lưu bản nháp khoa phòng${snapshot.code ? ` ${snapshot.code}` : ''}.`);
  };

  return (
    <AdminPortalShell
      activeTab="Khoa phòng"
      breadcrumbs={[
        { label: 'Cấu hình Cơ sở' },
        { label: 'Danh mục Khoa phòng (SCR-ADM-DEP-01)', href: LIST_PATH },
        { label: 'Khai báo khoa phòng (SCR-ADM-DEP-02)' },
      ]}
    >
      <form onSubmit={handleSubmit(handleCreate)} noValidate>
        <main className="mx-auto max-w-[1720px] px-6 pb-28 pt-6">
          <Link
            to={LIST_PATH}
            className="inline-block rounded-lg border border-border bg-white px-3 py-1.5 text-xs text-sub hover:text-primary"
          >
            &lt; Danh mục Khoa phòng (SCR-ADM-DEP-01)
          </Link>

          <h1 className="mt-4 text-2xl font-bold text-navy">Khai báo khoa phòng</h1>
          <p className="mt-1 text-sm text-sub">
            Khởi tạo khoa mới liên kết cơ sở và thiết lập trạng thái hoạt động
          </p>

          {message ? (
            <p role="status" className="mt-4 rounded-lg bg-sky-50 px-4 py-2 text-sm text-primary">
              {message}
            </p>
          ) : null}

          {errorMsg ? (
            <p role="alert" className="mt-4 rounded-lg bg-rose-50 px-4 py-2 text-sm text-rose-700">
              {errorMsg}
            </p>
          ) : null}

          <div className="mt-6 grid gap-6 lg:grid-cols-[2fr_1fr]">
            <section className="space-y-8 rounded-xl border border-border bg-white p-6">
              <fieldset className="space-y-4">
                <legend className="text-xs font-semibold uppercase tracking-wide text-navy">
                  Thông tin khoa
                </legend>

                <div>
                  <label htmlFor="dep-code" className="mb-1 block text-sm font-medium text-ink">
                    Mã khoa phòng *
                  </label>
                  <input
                    id="dep-code"
                    type="text"
                    placeholder="DEP-015"
                    aria-invalid={errors.code ? true : undefined}
                    className={`${inputClass} font-mono`}
                    {...register('code')}
                  />
                  {errors.code ? (
                    <p role="alert" className="mt-1 text-xs text-red-600">
                      {errors.code.message}
                    </p>
                  ) : null}
                </div>

                <div>
                  <label htmlFor="dep-name" className="mb-1 block text-sm font-medium text-ink">
                    Tên khoa phòng *
                  </label>
                  <input
                    id="dep-name"
                    type="text"
                    placeholder="Khoa Nội Tổng hợp"
                    aria-invalid={errors.name ? true : undefined}
                    className={inputClass}
                    {...register('name')}
                  />
                  {errors.name ? (
                    <p role="alert" className="mt-1 text-xs text-red-600">
                      {errors.name.message}
                    </p>
                  ) : null}
                </div>

                <div>
                  <label htmlFor="dep-facility" className="mb-1 block text-sm font-medium text-ink">
                    Cơ sở *
                  </label>
                  <select
                    id="dep-facility"
                    aria-invalid={errors.facilityId ? true : undefined}
                    className={inputClass}
                    {...register('facilityId')}
                  >
                    <option value="">Chọn cơ sở</option>
                    {facilities.map((facility) => (
                      <option key={facility.id} value={String(facility.id)}>
                        {facility.name}
                      </option>
                    ))}
                  </select>
                  {errors.facilityId ? (
                    <p role="alert" className="mt-1 text-xs text-red-600">
                      {errors.facilityId.message}
                    </p>
                  ) : null}
                </div>
              </fieldset>

              <fieldset className="space-y-3">
                <legend className="text-xs font-semibold uppercase tracking-wide text-navy">
                  Trạng thái khoa
                </legend>
                <label htmlFor="dep-status" className="block text-sm font-medium text-ink">
                  Trạng thái *
                </label>
                <select id="dep-status" className={inputClass} {...register('status')}>
                  <option value="ACTIVE">ACTIVE - Đang hoạt động</option>
                  <option value="INACTIVE">INACTIVE - Tạm ngừng</option>
                </select>
                <p className="text-xs text-sub">Không dùng vào lịch mới khi tạm ngừng</p>
              </fieldset>
            </section>

            <aside
              aria-label="Xem trước khoa phòng"
              className="h-fit rounded-xl border border-border bg-white p-6"
            >
              <div className="flex items-center gap-2 text-navy">
                <Building2 className="size-5" aria-hidden="true" />
                <h2 className="text-sm font-semibold">Xem trước</h2>
              </div>
              <dl className="mt-4 space-y-3 text-sm">
                <div>
                  <dt className="text-xs text-sub">Mã khoa</dt>
                  <dd data-testid="preview-code" className="font-mono text-ink">
                    {code || '—'}
                  </dd>
                </div>
                <div>
                  <dt className="text-xs text-sub">Tên khoa</dt>
                  <dd data-testid="preview-name" className="text-ink">
                    {name || '—'}
                  </dd>
                </div>
                <div>
                  <dt className="text-xs text-sub">Cơ sở</dt>
                  <dd data-testid="preview-facility" className="text-ink">
                    {facilityName ?? '—'}
                  </dd>
                </div>
                <div>
                  <dt className="text-xs text-sub">Trạng thái</dt>
                  <dd data-testid="preview-status" className="text-ink">
                    {STATUS_LABELS[status]}
                  </dd>
                </div>
              </dl>
            </aside>
          </div>
        </main>

        <div className="fixed inset-x-0 bottom-0 z-20 border-t border-border bg-white px-6 py-3">
          <div className="mx-auto flex max-w-[1720px] justify-end gap-3">
            <button
              type="button"
              aria-label="Hủy bỏ"
              onClick={() => guardAction(() => navigate(LIST_PATH))}
              className="rounded-lg border border-border px-4 py-2 text-sm text-sub hover:text-ink"
            >
              Hủy bỏ
            </button>
            <button
              type="button"
              aria-label="Lưu bản nháp"
              onClick={handleSaveDraft}
              className="rounded-lg border border-border px-4 py-2 text-sm text-navy hover:bg-canvas"
            >
              Lưu bản nháp
            </button>
            <button
              type="submit"
              aria-label="Khai báo khoa phòng"
              disabled={createMutation.isPending}
              className="rounded-lg bg-primary px-4 py-2 text-sm font-semibold text-white hover:bg-primary-dark disabled:opacity-50"
            >
              + Khai báo khoa phòng
            </button>
          </div>
        </div>
      </form>

      <UnsavedChangesModal open={isBlocked} onStay={cancelNavigation} onLeave={confirmNavigation} />
    </AdminPortalShell>
  );
}
