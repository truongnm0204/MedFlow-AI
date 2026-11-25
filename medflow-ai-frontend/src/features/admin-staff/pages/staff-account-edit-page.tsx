import { Loader2, Save } from 'lucide-react';
import { useMemo, useRef, useState, type FormEvent } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import {
  FormField,
  FormSectionCard,
  FormSelect,
  FormTextInput,
  formControlClass,
} from '@/shared/components/form/form-primitives';
import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { UnsavedChangesModal, useUnsavedChanges } from '@/shared/hooks/use-unsaved-changes';
import { Button } from '@/shared/ui/button';

import {
  STAFF_DEPARTMENTS,
  STAFF_FACILITIES,
  STAFF_ROLE_LABELS,
  STAFF_STATUS_LABELS,
} from '../data/staff-demo-data';
import { useAdminStaffAccounts, useUpdateStaffAccountMutation } from '../hooks/use-staff-accounts';
import { staffUpdateSchema } from '../schemas/staff-update.schema';
import type {
  StaffAccountRecord,
  StaffAccountStatus,
  StaffAuthMethod,
  StaffRole,
  UpdateStaffAccountInput,
} from '../types/staff-update.types';

type FieldErrors = Partial<Record<keyof UpdateStaffAccountInput, string>>;

const LIST_PATH = '/admin/staff';
const AUTH_METHODS: StaffAuthMethod[] = ['LOCAL', 'GOOGLE'];
const ROLES = Object.keys(STAFF_ROLE_LABELS) as StaffRole[];
const STATUSES = Object.keys(STAFF_STATUS_LABELS) as StaffAccountStatus[];

function pillClass(active: boolean) {
  return `rounded-md border px-3 py-2 text-xs font-semibold transition-colors ${
    active
      ? 'border-primary bg-primary text-white'
      : 'border-border bg-white text-sub hover:border-primary'
  }`;
}

function PreviewRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-3 border-b border-border py-2 text-xs last:border-b-0">
      <dt className="text-sub">{label}</dt>
      <dd className="text-right font-semibold text-ink">{value}</dd>
    </div>
  );
}

export function StaffAccountEditPage() {
  const { id = '' } = useParams<{ id: string }>();
  const { data, isLoading, isError, error } = useAdminStaffAccounts();

  const record = useMemo(() => {
    if (!id || !data?.records) return null;
    const normalized = id.trim().toLowerCase();
    return data.records.find((item) => item.staffCode.toLowerCase() === normalized) ?? null;
  }, [data?.records, id]);

  return (
    <AdminPortalShell
      activeTab="Bác sĩ & Nhân sự Y tế"
      breadcrumbs={[
        { label: 'Tài khoản nhân sự (SCR-ADM-STAFF-01)', href: LIST_PATH },
        { label: 'Cập nhật tài khoản' },
      ]}
    >
      {isLoading ? (
        <div className="flex items-center gap-2 p-6 text-sm text-sub">
          <Loader2 className="h-4 w-4 animate-spin text-primary" aria-hidden="true" />
          <span>Đang tải thông tin tài khoản nhân sự...</span>
        </div>
      ) : record ? (
        <StaffAccountEditForm key={record.staffCode} record={record} />
      ) : (
        <p role="alert" className="p-6 text-sm text-ink">
          {isError && error instanceof Error
            ? error.message
            : `Không tìm thấy tài khoản nhân sự ${id}.`}
        </p>
      )}
    </AdminPortalShell>
  );
}

function areStaffInputsEqual(a: UpdateStaffAccountInput, b: UpdateStaffAccountInput): boolean {
  return (
    a.fullName === b.fullName &&
    a.authMethod === b.authMethod &&
    a.facility === b.facility &&
    a.email === b.email &&
    a.status === b.status &&
    a.department === b.department &&
    a.role === b.role
  );
}

function StaffAccountEditForm({ record }: { record: StaffAccountRecord }) {
  const navigate = useNavigate();
  const updateMutation = useUpdateStaffAccountMutation();
  const { staffCode, ...initial } = record;
  const [values, setValues] = useState<UpdateStaffAccountInput>(() => ({ ...initial }));
  const [baselineValues, setBaselineValues] = useState<UpdateStaffAccountInput>(() => ({
    ...initial,
  }));
  const [errors, setErrors] = useState<FieldErrors>({});
  const [submitError, setSubmitError] = useState<string | null>(null);

  const isDirty = !areStaffInputsEqual(values, baselineValues);
  const { isBlocked, confirmNavigation, cancelNavigation, allowNavigation, guardAction } =
    useUnsavedChanges(isDirty);
  const latestValuesRef = useRef<UpdateStaffAccountInput>(values);

  const facilityOptions = useMemo(() => {
    const set = new Set<string>(STAFF_FACILITIES);
    if (record.facility) set.add(record.facility);
    return Array.from(set);
  }, [record.facility]);

  const departmentOptions = useMemo(() => {
    const set = new Set<string>(STAFF_DEPARTMENTS);
    if (record.department) set.add(record.department);
    return Array.from(set);
  }, [record.department]);

  const update = <K extends keyof UpdateStaffAccountInput>(
    key: K,
    value: UpdateStaffAccountInput[K],
  ) =>
    setValues((prev) => {
      const next = { ...prev, [key]: value };
      latestValuesRef.current = next;
      return next;
    });

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setSubmitError(null);
    const submittedSnapshot: UpdateStaffAccountInput = { ...values };
    const result = staffUpdateSchema.safeParse(submittedSnapshot);
    if (!result.success) {
      const next: FieldErrors = {};
      for (const issue of result.error.issues) {
        const key = issue.path[0] as keyof UpdateStaffAccountInput;
        next[key] ??= issue.message;
      }
      setErrors(next);
      return;
    }
    setErrors({});
    try {
      await updateMutation.mutateAsync({ staffIdentifier: staffCode, values: result.data });
      setBaselineValues(submittedSnapshot);
      const editedDuringSubmit = !areStaffInputsEqual(latestValuesRef.current, submittedSnapshot);
      if (!editedDuringSubmit) {
        allowNavigation();
        navigate(LIST_PATH);
      } else {
        guardAction(() => navigate(LIST_PATH));
      }
    } catch (err) {
      setSubmitError(err instanceof Error ? err.message : 'Không thể cập nhật tài khoản nhân sự.');
    }
  };

  return (
    <form
      onSubmit={(e) => void handleSubmit(e)}
      className="mx-auto w-full max-w-6xl px-6 pb-24 pt-4"
      noValidate
    >
      <h1 className="text-xl font-bold text-navy">Cập nhật tài khoản nhân sự</h1>
      <p className="mb-4 text-xs text-sub">
        Chỉnh sửa thông tin hồ sơ nhân sự, cơ sở, khoa công tác, vai trò và trạng thái tài khoản.
      </p>

      {submitError ? (
        <div
          role="alert"
          className="mb-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700"
        >
          {submitError}
        </div>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-[1fr_320px]">
        <div className="space-y-4">
          <FormSectionCard stepNumber={1} title="HỒ SƠ NHÂN SỰ & ĐĂNG NHẬP">
            <div className="grid gap-4 md:grid-cols-2">
              <FormField label="Mã nhân sự" required>
                <input
                  className={`${formControlClass} font-mono`}
                  value={staffCode}
                  readOnly
                  aria-readonly="true"
                />
              </FormField>
              <FormField label="Họ tên nhân sự" required error={errors.fullName}>
                <FormTextInput
                  value={values.fullName}
                  onChange={(e) => update('fullName', e.target.value)}
                />
              </FormField>
              <div
                role="group"
                aria-label="Phương thức đăng nhập"
                className="text-[11px] font-semibold text-ink"
              >
                <span>
                  Phương thức đăng nhập <span className="text-rose-500">*</span>
                </span>
                <div className="mt-1.5 flex gap-2">
                  {AUTH_METHODS.map((method) => (
                    <button
                      key={method}
                      type="button"
                      aria-pressed={values.authMethod === method}
                      aria-label={`Phương thức ${method}`}
                      className={pillClass(values.authMethod === method)}
                      onClick={() => update('authMethod', method)}
                    >
                      {method}
                    </button>
                  ))}
                </div>
              </div>
              <FormField label="Cơ sở" required error={errors.facility}>
                <FormSelect
                  value={values.facility}
                  onChange={(e) => update('facility', e.target.value)}
                >
                  {facilityOptions.map((item) => (
                    <option key={item} value={item}>
                      {item}
                    </option>
                  ))}
                </FormSelect>
              </FormField>
              <FormField
                label="Email đăng nhập"
                required
                error={errors.email}
                className="md:col-span-2"
              >
                <FormTextInput
                  type="email"
                  value={values.email}
                  onChange={(e) => update('email', e.target.value)}
                />
              </FormField>
            </div>
          </FormSectionCard>

          <FormSectionCard stepNumber={2} title="VAI TRÒ & TRẠNG THÁI">
            <div className="grid gap-4 md:grid-cols-2">
              <FormField label="Trạng thái tài khoản" required error={errors.status}>
                <FormSelect
                  value={values.status}
                  onChange={(e) => update('status', e.target.value as StaffAccountStatus)}
                >
                  {STATUSES.map((item) => (
                    <option key={item} value={item}>
                      {item} - {STAFF_STATUS_LABELS[item]}
                    </option>
                  ))}
                </FormSelect>
              </FormField>
              <FormField label="Khoa công tác" required error={errors.department}>
                <FormSelect
                  value={values.department}
                  onChange={(e) => update('department', e.target.value)}
                >
                  {departmentOptions.map((item) => (
                    <option key={item} value={item}>
                      {item}
                    </option>
                  ))}
                </FormSelect>
              </FormField>
              <div
                role="group"
                aria-label="Vai trò được gán"
                className="text-[11px] font-semibold text-ink md:col-span-2"
              >
                <span>
                  Vai trò được gán <span className="text-rose-500">*</span>
                </span>
                <div className="mt-1.5 flex flex-wrap gap-2">
                  {ROLES.map((role) => (
                    <button
                      key={role}
                      type="button"
                      aria-pressed={values.role === role}
                      aria-label={`Vai trò ${STAFF_ROLE_LABELS[role]}`}
                      className={pillClass(values.role === role)}
                      onClick={() => update('role', role)}
                    >
                      {STAFF_ROLE_LABELS[role]}
                    </button>
                  ))}
                </div>
              </div>
            </div>
          </FormSectionCard>
        </div>

        <aside
          aria-label="Xem trước tài khoản"
          className="h-fit rounded-xl border border-border bg-white p-4 shadow-sm"
        >
          <h2 className="mb-2 text-xs font-bold uppercase text-navy">Xem trước</h2>
          <dl data-testid="staff-preview">
            <PreviewRow label="Mã NV" value={staffCode} />
            <PreviewRow label="Họ tên" value={values.fullName || '—'} />
            <PreviewRow label="Email" value={values.email || '—'} />
            <PreviewRow label="Vai trò" value={STAFF_ROLE_LABELS[values.role]} />
            <PreviewRow label="Khoa phòng" value={values.department} />
            <PreviewRow label="Cơ sở" value={values.facility} />
            <PreviewRow label="Trạng thái" value={STAFF_STATUS_LABELS[values.status]} />
          </dl>
        </aside>
      </div>

      <div className="fixed inset-x-0 bottom-0 z-20 flex justify-end gap-3 border-t border-border bg-white px-6 py-3">
        <Button
          type="button"
          variant="secondary"
          aria-label="Hủy bỏ"
          onClick={() => guardAction(() => navigate(LIST_PATH))}
        >
          Hủy bỏ
        </Button>
        <Button type="submit" aria-label="Lưu thay đổi" disabled={updateMutation.isPending}>
          <Save className="h-4 w-4" aria-hidden="true" />
          Lưu thay đổi
        </Button>
      </div>

      <UnsavedChangesModal open={isBlocked} onStay={cancelNavigation} onLeave={confirmNavigation} />
    </form>
  );
}
