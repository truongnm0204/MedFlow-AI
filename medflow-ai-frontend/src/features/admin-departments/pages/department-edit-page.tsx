import { zodResolver } from '@hookform/resolvers/zod';
import { Building2 } from 'lucide-react';
import { useForm, useWatch } from 'react-hook-form';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

import { MOCK_DEPARTMENTS } from '../data/departments-data';
import {
  DEPARTMENT_FACILITIES,
  departmentUpdateSchema,
  type DepartmentUpdateFormValues,
} from '../schemas/department-update.schema';
import type { Department, DepartmentStatus } from '../types/department-update.types';

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

function DepartmentEditForm({ department }: { department: Department }) {
  const navigate = useNavigate();
  const {
    register,
    handleSubmit,
    control,
    formState: { errors, isSubmitting },
  } = useForm<DepartmentUpdateFormValues>({
    resolver: zodResolver(departmentUpdateSchema),
    defaultValues: {
      name: department.name,
      facility: department.facility as DepartmentUpdateFormValues['facility'],
      status: department.status,
    },
  });
  const watched = useWatch({ control });

  const onSubmit = (values: DepartmentUpdateFormValues) => {
    const target = MOCK_DEPARTMENTS.find((d) => d.code === department.code);
    if (target) Object.assign(target, values);
    navigate(LIST_PATH);
  };

  const inputClass =
    'w-full rounded-lg border border-border bg-white px-3 py-2 text-sm text-ink focus:border-primary focus:outline-none';
  const labelClass = 'mb-1 block text-sm font-semibold text-navy';

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate>
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
                {DEPARTMENT_FACILITIES.map((f) => (
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
          onClick={() => navigate(LIST_PATH)}
          className="rounded-lg border border-border px-4 py-2 text-sm font-semibold text-navy"
        >
          Hủy bỏ
        </button>
        <button
          type="submit"
          aria-label="Lưu thay đổi"
          disabled={isSubmitting}
          className="rounded-lg bg-primary px-4 py-2 text-sm font-semibold text-white hover:bg-primary-dark"
        >
          Lưu thay đổi
        </button>
      </div>
    </form>
  );
}

export function DepartmentEditPage() {
  const { id } = useParams<{ id: string }>();
  const department = MOCK_DEPARTMENTS.find((d) => d.code === id);

  return (
    <AdminPortalShell activeTab="Khoa phòng" breadcrumbs={BREADCRUMBS}>
      <main className="mx-auto max-w-[1280px] px-6 pb-8 pt-6">
        <h1 className="text-2xl font-bold text-navy">Chỉnh sửa khoa phòng</h1>
        <p className="mb-6 text-sm text-sub">
          Cập nhật thông tin khoa, liên kết cơ sở và trạng thái hoạt động
        </p>
        {department ? (
          <DepartmentEditForm key={department.code} department={department} />
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
