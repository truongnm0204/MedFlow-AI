import { zodResolver } from '@hookform/resolvers/zod';
import { Building2 } from 'lucide-react';
import { useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { Link, useNavigate } from 'react-router-dom';
import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import {
  departmentCreateSchema,
  type DepartmentCreateFormValues,
} from '../schemas/department-create.schema';
import type { CreateDepartmentInput, FacilityOption } from '../types/department-create.types';

const FACILITY_OPTIONS: FacilityOption[] = [
  { id: 'FAC-001', name: 'MedFlow Cơ sở 1 - Hà Nội' },
  { id: 'FAC-002', name: 'MedFlow Cơ sở 2 - TP.HCM' },
];

const STATUS_LABELS: Record<CreateDepartmentInput['status'], string> = {
  ACTIVE: 'Đang hoạt động',
  INACTIVE: 'Tạm ngừng',
};

const LIST_PATH = '/admin/departments';

const inputClass =
  'w-full rounded-lg border border-border bg-white px-3 py-2 text-sm text-ink outline-none focus:border-primary focus:ring-2 focus:ring-primary/20';

export function DepartmentCreatePage() {
  const navigate = useNavigate();
  const [message, setMessage] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    getValues,
    control,
    formState: { errors },
  } = useForm<DepartmentCreateFormValues>({
    resolver: zodResolver(departmentCreateSchema),
    defaultValues: { code: '', name: '', facilityId: '', status: 'ACTIVE' },
  });

  const code = useWatch({ control, name: 'code' });
  const name = useWatch({ control, name: 'name' });
  const facilityId = useWatch({ control, name: 'facilityId' });
  const status = useWatch({ control, name: 'status' });

  const facilityName = FACILITY_OPTIONS.find((f) => f.id === facilityId)?.name;

  const handleCreate = (values: DepartmentCreateFormValues) => {
    const input: CreateDepartmentInput = { ...values };
    setMessage(`Đã khai báo khoa phòng ${input.code}.`);
    navigate(LIST_PATH);
  };

  const handleSaveDraft = () => {
    const { code: draftCode } = getValues();
    setMessage(`Đã lưu bản nháp khoa phòng${draftCode ? ` ${draftCode}` : ''}.`);
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
                    placeholder="Khoa Hồi sức tích cực"
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
                    {FACILITY_OPTIONS.map((facility) => (
                      <option key={facility.id} value={facility.id}>
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
              onClick={() => navigate(LIST_PATH)}
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
              className="rounded-lg bg-primary px-4 py-2 text-sm font-semibold text-white hover:bg-primary-dark"
            >
              + Khai báo khoa phòng
            </button>
          </div>
        </div>
      </form>
    </AdminPortalShell>
  );
}
