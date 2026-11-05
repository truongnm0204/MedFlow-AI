import { zodResolver } from '@hookform/resolvers/zod';
import { Plus } from 'lucide-react';
import { useForm, useWatch } from 'react-hook-form';
import { useNavigate } from 'react-router-dom';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { Button } from '@/shared/ui/button';

import {
  STAFF_DEPARTMENTS,
  STAFF_FACILITIES,
  STAFF_ROLE_OPTIONS,
  staffCreateSchema,
  type StaffCreateFormValues,
} from '../schemas/staff-create.schema';

const LIST_PATH = '/admin/doctors';

const DEFAULT_VALUES: StaffCreateFormValues = {
  staffCode: '',
  fullName: '',
  authProvider: 'LOCAL',
  facility: STAFF_FACILITIES[0],
  email: '',
  password: '',
  status: 'ACTIVE',
  role: 'DOCTOR',
  department: STAFF_DEPARTMENTS[0],
};

const FIELD =
  'w-full rounded-lg border border-border bg-white px-3 py-2 text-sm text-ink outline-none focus:border-primary';

function FieldError({ message }: { message?: string }) {
  return message ? (
    <p role="alert" className="mt-1 text-xs text-danger">
      {message}
    </p>
  ) : null;
}

function PreviewRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-3 border-b border-border py-2 text-sm last:border-0">
      <dt className="text-sub">{label}</dt>
      <dd className="text-right font-semibold text-navy">{value || '—'}</dd>
    </div>
  );
}

export function StaffAccountCreatePage() {
  const navigate = useNavigate();
  const {
    register,
    handleSubmit,
    control,
    setValue,
    formState: { errors },
  } = useForm<StaffCreateFormValues>({
    resolver: zodResolver(staffCreateSchema),
    defaultValues: DEFAULT_VALUES,
  });

  const values = useWatch({ control });
  const roleLabel = STAFF_ROLE_OPTIONS.find((r) => r.value === values.role)?.label ?? '';
  const isLocal = values.authProvider === 'LOCAL';

  const onSubmit = () => {
    navigate(LIST_PATH);
  };

  return (
    <AdminPortalShell
      showTabs={false}
      activeTab="Bác sĩ & Nhân sự Y tế"
      breadcrumbText="Tài khoản nhân sự (SCR-ADM-STAFF-01)"
      breadcrumbLink={LIST_PATH}
    >
      <form noValidate onSubmit={handleSubmit(onSubmit)} className="flex min-h-full flex-col">
        <div className="flex-1 p-6">
          <h1 className="text-2xl font-bold text-navy">Tạo tài khoản nhân sự</h1>
          <p className="mt-1 text-sm text-sub">
            Liên kết hồ sơ nhân sự với email đăng nhập, cơ sở, khoa công tác và vai trò
          </p>

          <div className="mt-6 grid gap-6 lg:grid-cols-[1fr_360px]">
            <div className="space-y-6">
              <section className="rounded-xl border border-border bg-white p-5">
                <h2 className="mb-4 text-xs font-bold tracking-wider text-sub uppercase">
                  Hồ sơ nhân sự &amp; đăng nhập
                </h2>
                <div className="grid gap-4 md:grid-cols-2">
                  <div>
                    <label
                      htmlFor="staffCode"
                      className="mb-1 block text-sm font-semibold text-navy"
                    >
                      Mã nhân sự *
                    </label>
                    <input
                      id="staffCode"
                      placeholder="STF-0250"
                      className={`${FIELD} font-mono`}
                      {...register('staffCode')}
                    />
                    <FieldError message={errors.staffCode?.message} />
                  </div>
                  <div>
                    <label
                      htmlFor="fullName"
                      className="mb-1 block text-sm font-semibold text-navy"
                    >
                      Họ tên nhân sự *
                    </label>
                    <input
                      id="fullName"
                      placeholder="Lê Minh An"
                      className={FIELD}
                      {...register('fullName')}
                    />
                    <FieldError message={errors.fullName?.message} />
                  </div>
                  <fieldset className="md:col-span-2">
                    <legend className="mb-1 text-sm font-semibold text-navy">
                      Phương thức đăng nhập *
                    </legend>
                    <div className="flex gap-4 text-sm text-ink">
                      <label className="flex items-center gap-2">
                        <input type="radio" value="LOCAL" {...register('authProvider')} />
                        LOCAL (Email/Mật khẩu nội bộ)
                      </label>
                      <label className="flex items-center gap-2">
                        <input type="radio" value="GOOGLE" {...register('authProvider')} />
                        GOOGLE
                      </label>
                    </div>
                  </fieldset>
                  <div>
                    <label
                      htmlFor="facility"
                      className="mb-1 block text-sm font-semibold text-navy"
                    >
                      Cơ sở *
                    </label>
                    <select id="facility" className={FIELD} {...register('facility')}>
                      {STAFF_FACILITIES.map((f) => (
                        <option key={f} value={f}>
                          {f}
                        </option>
                      ))}
                    </select>
                    <FieldError message={errors.facility?.message} />
                  </div>
                  <div>
                    <label htmlFor="email" className="mb-1 block text-sm font-semibold text-navy">
                      Email đăng nhập *
                    </label>
                    <input
                      id="email"
                      type="email"
                      placeholder="an.le@medflow.vn"
                      className={FIELD}
                      {...register('email')}
                    />
                    <FieldError message={errors.email?.message} />
                  </div>
                  {isLocal && (
                    <div>
                      <label
                        htmlFor="password"
                        className="mb-1 block text-sm font-semibold text-navy"
                      >
                        Mật khẩu tạm thời *
                      </label>
                      <input
                        id="password"
                        type="password"
                        autoComplete="new-password"
                        className={FIELD}
                        {...register('password')}
                      />
                      <FieldError message={errors.password?.message} />
                    </div>
                  )}
                </div>
              </section>

              <section className="rounded-xl border border-border bg-white p-5">
                <h2 className="mb-4 text-xs font-bold tracking-wider text-sub uppercase">
                  Vai trò &amp; trạng thái
                </h2>
                <div className="grid gap-4 md:grid-cols-2">
                  <fieldset>
                    <legend className="mb-1 text-sm font-semibold text-navy">
                      Trạng thái tài khoản *
                    </legend>
                    <div className="flex gap-4 text-sm text-ink">
                      <label className="flex items-center gap-2">
                        <input type="radio" value="ACTIVE" {...register('status')} />
                        ACTIVE (Hoạt động)
                      </label>
                      <label className="flex items-center gap-2">
                        <input type="radio" value="LOCKED" {...register('status')} />
                        LOCKED (Đã khóa)
                      </label>
                    </div>
                  </fieldset>
                  <div>
                    <label
                      htmlFor="department"
                      className="mb-1 block text-sm font-semibold text-navy"
                    >
                      Khoa công tác *
                    </label>
                    <select id="department" className={FIELD} {...register('department')}>
                      {STAFF_DEPARTMENTS.map((d) => (
                        <option key={d} value={d}>
                          {d}
                        </option>
                      ))}
                    </select>
                    <FieldError message={errors.department?.message} />
                  </div>
                  <div className="md:col-span-2">
                    <span className="mb-1 block text-sm font-semibold text-navy">
                      Vai trò được gán *
                    </span>
                    <div className="flex flex-wrap gap-2">
                      {STAFF_ROLE_OPTIONS.map((r) => {
                        const selected = values.role === r.value;
                        return (
                          <button
                            key={r.value}
                            type="button"
                            aria-pressed={selected}
                            aria-label={`Vai trò ${r.label}`}
                            onClick={() => setValue('role', r.value, { shouldValidate: true })}
                            className={`rounded-full border px-4 py-1.5 text-sm font-semibold ${
                              selected
                                ? 'border-primary bg-primary text-white'
                                : 'border-border bg-white text-navy hover:bg-canvas'
                            }`}
                          >
                            {r.label}
                          </button>
                        );
                      })}
                    </div>
                    <FieldError message={errors.role?.message} />
                  </div>
                </div>
              </section>
            </div>

            <aside
              aria-label="Xem trước tài khoản"
              className="h-fit rounded-xl border border-border bg-white p-5"
            >
              <h2 className="mb-2 text-xs font-bold tracking-wider text-sub uppercase">
                Xem trước tài khoản
              </h2>
              <dl>
                <PreviewRow label="Mã NV" value={values.staffCode ?? ''} />
                <PreviewRow label="Họ tên" value={values.fullName ?? ''} />
                <PreviewRow label="Email" value={values.email ?? ''} />
                <PreviewRow label="Vai trò" value={roleLabel} />
                <PreviewRow label="Khoa phòng" value={values.department ?? ''} />
                <PreviewRow label="Cơ sở" value={values.facility ?? ''} />
              </dl>
            </aside>
          </div>
        </div>

        <div className="sticky bottom-0 z-20 flex justify-end gap-2.5 border-t border-border bg-white px-6 py-3 shadow-lg">
          <Button
            type="button"
            variant="secondary"
            size="sm"
            aria-label="Hủy bỏ"
            onClick={() => navigate(LIST_PATH)}
          >
            Hủy bỏ
          </Button>
          <Button type="submit" size="sm" aria-label="Tạo tài khoản" className="gap-1.5 font-bold">
            <Plus className="h-4 w-4" aria-hidden="true" />
            <span>Tạo tài khoản</span>
          </Button>
        </div>
      </form>
    </AdminPortalShell>
  );
}
