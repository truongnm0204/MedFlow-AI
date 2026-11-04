import { zodResolver } from '@hookform/resolvers/zod';
import { Clock, Info, RotateCcw, Save, Undo2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert, Button, Spinner, toast } from '@/shared/ui';

import { AppointmentPolicyForm } from '../components/appointment-policy-form';
import { AppointmentPolicySidebar } from '../components/appointment-policy-sidebar';
import { DEFAULT_APPOINTMENT_POLICY_VALUES } from '../data/appointment-policy-mock';
import { useAppointmentPolicy, useUpdateAppointmentPolicy } from '../hooks/use-appointment-policy';
import {
  appointmentPolicySchema,
  type AppointmentPolicyFormValues,
} from '../schemas/appointment-policy.schema';
import type { AppointmentPolicyDto } from '../types/appointment-policy.types';

function extractFormValues(dto: AppointmentPolicyDto): AppointmentPolicyFormValues {
  return {
    bookingWindowDays: dto.bookingWindowDays,
    bookingLeadHours: dto.bookingLeadHours,
    maxActiveBookings: dto.maxActiveBookings,
    rescheduleMinimumHours: dto.rescheduleMinimumHours,
    maxReschedules: dto.maxReschedules,
    cancelMinimumHours: dto.cancelMinimumHours,
    noShowGraceMinutes: dto.noShowGraceMinutes,
    noShowMaxCount: dto.noShowMaxCount,
    noShowLookbackDays: dto.noShowLookbackDays,
    noShowBlockDays: dto.noShowBlockDays,
  };
}

export function AppointmentPolicyPage() {
  const user = useAuthStore((state) => state.user);
  const policyQuery = useAppointmentPolicy();
  const updateMutation = useUpdateAppointmentPolicy();

  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    control,
    formState: { errors, isDirty },
  } = useForm<AppointmentPolicyFormValues>({
    resolver: zodResolver(appointmentPolicySchema),
    defaultValues: DEFAULT_APPOINTMENT_POLICY_VALUES,
  });

  const watchedValues = useWatch({
    control,
    defaultValue: DEFAULT_APPOINTMENT_POLICY_VALUES,
  }) as AppointmentPolicyFormValues;
  useEffect(() => {
    if (policyQuery.data) {
      reset(extractFormValues(policyQuery.data), { keepDirtyValues: true });
    }
  }, [policyQuery.data, reset]);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.ctrlKey && e.key.toLowerCase() === 's') {
        e.preventDefault();
        document.getElementById('appointment-policy-submit')?.click();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);

  const isSystemAdminOnly =
    Boolean(user?.roles.includes('SYSTEM_ADMIN')) && !user?.roles.includes('HOSPITAL_ADMIN');

  const handleSave = async (values: AppointmentPolicyFormValues) => {
    setStatusMessage(null);
    setErrorMessage(null);
    try {
      const updated = await updateMutation.mutateAsync(values);
      reset(extractFormValues(updated));
      setStatusMessage('Đã lưu cấu hình chính sách đặt lịch thành công.');
      toast.success('Đã cập nhật chính sách đặt lịch khám cho cơ sở.', 'Lưu cấu hình thành công');
    } catch (err) {
      const msg = getErrorMessage(err, 'Không thể cập nhật chính sách đặt lịch.');
      setErrorMessage(msg);
      toast.error(msg, 'Lỗi cập nhật chính sách');
    }
  };

  const handleRestoreDefaults = () => {
    setErrorMessage(null);
    reset(DEFAULT_APPOINTMENT_POLICY_VALUES, { keepDefaultValues: true });
    setStatusMessage('Đã điền giá trị mặc định chuẩn hệ thống. Nhấn Lưu cấu hình để áp dụng.');
  };

  const handleDiscardChanges = () => {
    setStatusMessage(null);
    setErrorMessage(null);
    const baseline = policyQuery.data
      ? extractFormValues(policyQuery.data)
      : DEFAULT_APPOINTMENT_POLICY_VALUES;
    reset(baseline);
  };

  return (
    <AdminPortalShell
      activeTab="Chính sách hẹn"
      showTabs={false}
      breadcrumbs={[
        { label: 'Cấu hình Cơ sở' },
        { label: 'Chính sách đặt lịch khám (SCR-ADM-POL-01)' },
      ]}
    >
      <form onSubmit={handleSubmit(handleSave)} noValidate>
        <main className="px-6 pb-28 pt-6">
          {/* Subheader bar */}
          <div className="flex flex-wrap items-center justify-between gap-3 text-xs">
            <div className="flex flex-wrap items-center gap-3">
              <span className="rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-[11px] font-semibold text-primary">
                ● Cấu hình theo cơ sở (UC54)
              </span>
              <span className="font-mono text-[11px] text-sub">Mã màn hình: SCR-ADM-POL-01</span>
            </div>

            <div className="flex items-center gap-2 text-xs text-sub">
              <Clock className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
              <span>{isDirty ? 'Thay đổi chưa lưu' : 'Đồng bộ với cấu hình hiện hành'}</span>
            </div>
          </div>

          {/* Heading */}
          <div className="mt-4">
            <h1 className="text-2xl font-bold tracking-tight text-ink">
              Cấu hình Chính sách Đặt lịch Khám
            </h1>
            <p className="mt-1 text-xs text-sub">
              Thiết lập cửa sổ đặt hẹn, giới hạn số lịch hoạt động, thời hạn đổi/hủy trực tuyến và
              ngưỡng khóa đặt lịch khi bệnh nhân vắng mặt (No-show) trong phạm vi cơ sở y tế.
            </p>
          </div>

          {/* Operational Guidance Banner */}
          <div className="my-5 flex items-start gap-3 rounded-lg border border-sky-200 bg-sky-50/70 p-4 text-xs leading-relaxed text-sky-900 shadow-xs">
            <span className="mt-0.5 grid h-6 w-6 shrink-0 place-items-center rounded-full bg-white text-sky-600 shadow-2xs">
              <Info className="h-4 w-4" aria-hidden="true" />
            </span>
            <p>
              <span className="font-bold">Lưu ý vận hành Chính sách Đặt hẹn:</span> Các tham số tại
              đây kiểm soát trực tiếp điều kiện đặt lịch mới (UC19), đổi lịch (UC21), hủy lịch
              (UC22) và tự động khóa tài khoản vượt ngưỡng vắng mặt. Việc lưu cấu hình tạo phiên bản
              mới trong <code className="font-mono">hospital_policies</code> và ghi nhận nhật ký
              kiểm toán.
            </p>
          </div>

          {isSystemAdminOnly && (
            <Alert tone="info" className="mb-5">
              <div className="font-bold">Tài khoản Quản trị hệ thống (SYSTEM_ADMIN)</div>
              <div className="mt-0.5">
                Chính sách đặt lịch được cấu hình riêng theo từng bệnh viện bởi Quản trị viên cơ sở
                (HOSPITAL_ADMIN).
              </div>
            </Alert>
          )}

          {statusMessage && (
            <div role="status" className="mb-5">
              <Alert tone="success">{statusMessage}</Alert>
            </div>
          )}

          {errorMessage && (
            <div role="alert" className="mb-5">
              <Alert tone="error">{errorMessage}</Alert>
            </div>
          )}

          {policyQuery.isPending ? (
            <div className="flex flex-col items-center justify-center rounded-xl border border-border bg-white py-16 text-xs text-sub shadow-sm">
              <Spinner className="h-6 w-6 text-primary" />
              <span className="mt-3 font-medium">Đang tải cấu hình chính sách đặt lịch...</span>
            </div>
          ) : policyQuery.isError ? (
            <div role="alert" className="mb-5">
              <Alert tone="error">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div>
                    <div className="font-bold">Không thể tải cấu hình chính sách</div>
                    <div className="mt-0.5">
                      {getErrorMessage(
                        policyQuery.error,
                        'Không thể tải cấu hình chính sách đặt lịch của cơ sở.',
                      )}
                    </div>
                  </div>
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    onClick={() => void policyQuery.refetch()}
                  >
                    Thử tải lại
                  </Button>
                </div>
              </Alert>
            </div>
          ) : (
            <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1.65fr)_minmax(340px,1fr)]">
              <AppointmentPolicyForm
                register={register}
                errors={errors}
                disabled={updateMutation.isPending}
              />

              <AppointmentPolicySidebar
                values={watchedValues}
                lastUpdatedAt={policyQuery.data?.lastUpdatedAt ?? null}
                isDirty={isDirty}
              />
            </div>
          )}
        </main>

        {/* Sticky Action Bar */}
        <div className="sticky bottom-0 z-20 border-t border-border bg-white px-6 py-3 shadow-lg">
          <div className="flex flex-wrap items-center justify-between gap-4 text-xs text-sub">
            <div>
              <span>
                Thay đổi chính sách có hiệu lực ngay cho các giao dịch đặt/đổi/hủy lịch tiếp theo.
              </span>
            </div>

            <div className="flex flex-wrap items-center gap-2.5">
              <Button
                type="button"
                variant="secondary"
                size="sm"
                onClick={handleRestoreDefaults}
                disabled={updateMutation.isPending || policyQuery.isPending}
                className="gap-1.5"
              >
                <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Khôi phục mặc định</span>
              </Button>
              <Button
                type="button"
                variant="secondary"
                size="sm"
                onClick={handleDiscardChanges}
                disabled={!isDirty || updateMutation.isPending || policyQuery.isPending}
                className="gap-1.5"
              >
                <Undo2 className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Hoàn tác thay đổi</span>
              </Button>
              <Button
                id="appointment-policy-submit"
                type="submit"
                size="sm"
                loading={updateMutation.isPending}
                disabled={policyQuery.isPending || policyQuery.isError}
                className="gap-1.5 font-bold"
              >
                <Save className="h-4 w-4" aria-hidden="true" />
                <span>Lưu cấu hình chính sách [Ctrl+S]</span>
              </Button>
            </div>
          </div>
        </div>
      </form>
    </AdminPortalShell>
  );
}
