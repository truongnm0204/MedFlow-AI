import { zodResolver } from '@hookform/resolvers/zod';
import { Building2 } from 'lucide-react';
import { useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { UnsavedChangesModal, useUnsavedChanges } from '@/shared/hooks/use-unsaved-changes';

import { useAdminDepartments, useUpdateDepartmentMutation } from '../hooks/use-departments';
import {
  DEPARTMENT_FACILITIES,
  departmentUpdateSchema,
  type DepartmentUpdateFormValues,
} from '../schemas/department-update.schema';
import type { DepartmentStatus } from '../types/department-update.types';
import type { DepartmentItem } from '../types/department.types';

const LIST_PATH = '/admin/departments';

const STATUS_OPTIONS: { value: DepartmentStatus; label: string }[] = [
  { value: 'ACTIVE', label: 'ACTIVE - Đang hoạt động' },
  { value: 'INACTIVE', label: 'INACTIVE - Tạm ngừng' },
];

const BREADCRUMBS = [
  { label: 'Danh mục Khoa phòng (SCR-ADM-DEP-01)', href: LIST_PATH },
  { label: 'Chỉnh sửa khoa phòng' },
];

function statusLabel(status: DepartmentStatus) {
  return status === 'ACTIVE' ? 'Đang hoạt động' : 'Tạm ngừng';
}

function DepartmentEditForm({
  department,
  facilities,
}: {
  department: DepartmentItem;
  facilities: string[];
}) {
  const navigate = useNavigate();
  const updateMutation = useUpdateDepartmentMutation();
  const [submitError, setSubmitError] = useState<string | null>(null);

  const facilityChoices = Array.from(
    new Set([department.facility, ...facilities, ...DEPARTMENT_FACILITIES].filter(Boolean)),
  );

  const {
    register,
    handleSubmit,
    getValues,
    reset,
    control,
    formState: { errors, isSubmitting, isDirty },
  } = useForm<DepartmentUpdateFormValues>({
    resolver: zodResolver(departmentUpdateSchema),
    defaultValues: {
      name: department.name,
      facility: (department.facility ||
        DEPARTMENT_FACILITIES[0]) as DepartmentUpdateFormValues['facility'],
      status: department.status,
    },
  });
  const watched = useWatch({ control });
  const { isBlocked, confirmNavigation, cancelNavigation, allowNavigation, guardAction } =
    useUnsavedChanges(isDirty);

  const onSubmit = async (values: DepartmentUpdateFormValues) => {
    setSubmitError(null);
    const rawSnapshot = structuredClone(getValues());
    try {
      await updateMutation.mutateAsync({
        id: department.id,
        payload: {
          code: department.code,
          name: values.name.trim(),
          isActive: values.status === 'ACTIVE',
        },
      });
      const editedDuringSubmit = JSON.stringify(getValues()) !== JSON.stringify(rawSnapshot);
      reset(rawSnapshot, { keepValues: true });
      if (!editedDuringSubmit) {
        allowNavigation();
        navigate(LIST_PATH);
      } else {
        guardAction(() => navigate(LIST_PATH));
      }
    } catch (error) {
      setSubmitError(
        error instanceof Error ? error.message : 'Không thể cập nhật thông tin khoa phòng.',
      );
    }
  };

  const inputClass =
    'w-full rounded-lg border border-border bg-white px-3 py-2 text-sm text-ink focus:border-primary focus:outline-none';
  const labelClass = 'mb-1 block text-sm font-semibold text-navy';

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate>
      {submitError && (
        <p role="alert" className="mb-4 rounded-lg bg-rose-50 px-4 py-2 text-xs text-rose-700">
          {submitError}
        </p>
      )}
      <div className="grid gap-6 lg:grid-cols-[2fr_1fr]">
        <div className="space-y-6 rounded-xl border border-border bg-white p-6">
          <section className="space-y-4">
            <h2 className="text-xs font-bold uppercase tracking-wide text-sub">THÔNG TIN KHOA</h2>
            <div>
              <label htmlFor="dep-code" className={labelClass}>
                Mã khoa phòng *
              </label>
              <input
                id="dep-code"
                readOnly
                value={department.code}
                className={`${inputClass} bg-canvas font-mono`}
              />
            </div>
            <div>
              <label htmlFor="dep-name" className={labelClass}>
                Tên khoa phòng *
              </label>
              <input id="dep-name" className={inputClass} {...register('name')} />
              {errors.name && (
                <p role="alert" className="mt-1 text-xs text-red-600">
                  {errors.name.message}
                </p>
              )}
            </div>
            <div>
              <label htmlFor="dep-facility" className={labelClass}>
                Cơ sở *
              </label>
              <select id="dep-facility" className={inputClass} {...register('facility')}>
                {facilityChoices.map((f) => (
                  <option key={f} value={f}>
                    {f}
                  </option>
                ))}
              </select>
              {errors.facility && (
                <p role="alert" className="mt-1 text-xs text-red-600">
                  {errors.facility.message}
                </p>
              )}
            </div>
          </section>
          <section className="space-y-3">
            <h2 className="text-xs font-bold uppercase tracking-wide text-sub">TRẠNG THÁI KHOA</h2>
            <fieldset>
              <legend className={labelClass}>Trạng thái *</legend>
              <div className="flex flex-wrap gap-4">
                {STATUS_OPTIONS.map((opt) => (
                  <label key={opt.value} className="flex items-center gap-2 text-sm text-ink">
                    <input type="radio" value={opt.value} {...register('status')} />
                    {opt.label}
                  </label>
                ))}
              </div>
            </fieldset>
            <p className="text-xs text-sub">Không dùng vào lịch mới khi tạm ngừng</p>
          </section>
        </div>

        <aside
          aria-label="Xem trước khoa phòng"
          className="h-fit space-y-3 rounded-xl border border-border bg-white p-6"
        >
          <h2 className="flex items-center gap-2 text-sm font-bold text-navy">
            <Building2 className="h-4 w-4" aria-hidden="true" />
            Xem trước
          </h2>
          <dl className="space-y-2 text-sm">
            <div>
              <dt className="text-sub">Mã khoa</dt>
              <dd data-testid="preview-code" className="font-mono text-ink">
                {department.code}
              </dd>
            </div>
            <div>
              <dt className="text-sub">Tên khoa</dt>
              <dd data-testid="preview-name" className="text-ink">
                {watched.name}
              </dd>
            </div>
            <div>
              <dt className="text-sub">Cơ sở</dt>
              <dd data-testid="preview-facility" className="text-ink">
                {watched.facility}
              </dd>
            </div>
            <div>
              <dt className="text-sub">Trạng thái</dt>
              <dd data-testid="preview-status" className="text-ink">
                {watched.status ? statusLabel(watched.status) : ''}
              </dd>
            </div>
          </dl>
        </aside>
      </div>

      <div className="sticky bottom-0 mt-6 flex justify-end gap-3 border-t border-border bg-white px-6 py-4">
        <button
          type="button"
          aria-label="Hủy bỏ"
          onClick={() => guardAction(() => navigate(LIST_PATH))}
          className="rounded-lg border border-border px-4 py-2 text-sm font-semibold text-navy"
        >
          Hủy bỏ
        </button>
        <button
          type="submit"
          aria-label="Lưu thay đổi"
          disabled={isSubmitting || updateMutation.isPending}
          className="rounded-lg bg-primary px-4 py-2 text-sm font-semibold text-white hover:bg-primary-dark disabled:opacity-50"
        >
          Lưu thay đổi
        </button>
      </div>

      <UnsavedChangesModal open={isBlocked} onStay={cancelNavigation} onLeave={confirmNavigation} />
    </form>
  );
}

export function DepartmentEditPage() {
  const { id } = useParams<{ id: string }>();
  const departmentsQuery = useAdminDepartments();
  const departments = departmentsQuery.data ?? [];
  const department = departments.find((d) => d.id === id || d.code === id);
  const facilities = Array.from(new Set(departments.map((d) => d.facility)));

  return (
    <AdminPortalShell activeTab="Khoa phòng" breadcrumbs={BREADCRUMBS}>
      <main className="mx-auto max-w-[1280px] px-6 pb-8 pt-6">
        <h1 className="text-2xl font-bold text-navy">Chỉnh sửa khoa phòng</h1>
        <p className="mb-6 text-sm text-sub">
          Cập nhật thông tin khoa, liên kết cơ sở và trạng thái hoạt động
        </p>
        {departmentsQuery.isPending ? (
          <p role="status" className="text-sm text-sub">
            Đang tải thông tin khoa phòng...
          </p>
        ) : department ? (
          <DepartmentEditForm key={department.id} department={department} facilities={facilities} />
        ) : (
          <div role="alert" className="rounded-xl border border-border bg-white p-6 text-sm">
            Không tìm thấy khoa phòng {id}.{' '}
            <Link to={LIST_PATH} className="text-primary underline">
              Quay lại danh mục
            </Link>
          </div>
        )}
      </main>
    </AdminPortalShell>
  );
}
