import { zodResolver } from '@hookform/resolvers/zod';
import axios from 'axios';
import { Clock, Info, RotateCcw, Save, Undo2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert, Button, Spinner, toast } from '@/shared/ui';

import { QueuePolicyForm } from '../components/queue-policy-form';
import { QueuePolicySidebar } from '../components/queue-policy-sidebar';
import { DEFAULT_QUEUE_POLICY_VALUES } from '../data/queue-policy-defaults';
import { useQueuePolicy, useUpdateQueuePolicy } from '../hooks/use-queue-policy';
import { queuePolicySchema, type QueuePolicyFormValues } from '../schemas/queue-policy.schema';
import type { QueuePolicyDto } from '../types/queue-policy.types';

function extractFormValues(dto: QueuePolicyDto): QueuePolicyFormValues {
  return {
    graceMinutes: dto.graceMinutes,
    expectedDurationMinutes: dto.expectedDurationMinutes,
    safetyBufferMinutes: dto.safetyBufferMinutes,
    walkinPriorityRatio: {
      priority: dto.walkinPriorityRatio.priority,
      normal: dto.walkinPriorityRatio.normal,
    },
    publicNumberImmutable: dto.publicNumberImmutable,
    manualPriorityRequiresVerification: dto.manualPriorityRequiresVerification,
  };
}

function getQueuePolicyErrorMessage(error: unknown, fallback: string): string {
  if (axios.isAxiosError(error) && error.response?.status === 404) {
    return 'Máy chủ chưa hỗ trợ API cấu hình chính sách hàng đợi (/api/v1/admin/queue-policy).';
  }
  return getErrorMessage(error, fallback);
}

export function QueuePolicyPage() {
  const user = useAuthStore((state) => state.user);
  const policyQuery = useQueuePolicy();
  const updateMutation = useUpdateQueuePolicy();

  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    control,
    formState: { errors, isDirty },
  } = useForm<QueuePolicyFormValues>({
    resolver: zodResolver(queuePolicySchema),
    defaultValues: DEFAULT_QUEUE_POLICY_VALUES,
  });

  const watchedValues = useWatch({
    control,
    defaultValue: DEFAULT_QUEUE_POLICY_VALUES,
  }) as QueuePolicyFormValues;

  useEffect(() => {
    if (policyQuery.data) {
      reset(extractFormValues(policyQuery.data), { keepDirtyValues: true });
    }
  }, [policyQuery.data, reset]);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.ctrlKey && e.key.toLowerCase() === 's') {
        e.preventDefault();
        document.getElementById('queue-policy-submit')?.click();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);

  const isSystemAdminOnly =
    Boolean(user?.roles.includes('SYSTEM_ADMIN')) && !user?.roles.includes('HOSPITAL_ADMIN');

  const handleSave = async (values: QueuePolicyFormValues) => {
    setStatusMessage(null);
    setErrorMessage(null);
    try {
      const updated = await updateMutation.mutateAsync(values);
      reset(extractFormValues(updated));
      setStatusMessage('Đã lưu cấu hình chính sách hàng đợi thành công.');
      toast.success('Đã cập nhật chính sách hàng đợi khám cho cơ sở.', 'Lưu cấu hình thành công');
    } catch (err) {
      const msg = getQueuePolicyErrorMessage(err, 'Không thể cập nhật chính sách hàng đợi.');
      setErrorMessage(msg);
      toast.error(msg, 'Lỗi cập nhật chính sách');
    }
  };

  const handleRestoreDefaults = () => {
    setErrorMessage(null);
    reset(DEFAULT_QUEUE_POLICY_VALUES, { keepDefaultValues: true });
    setStatusMessage('Đã điền giá trị mặc định chuẩn hệ thống. Nhấn Lưu cấu hình để áp dụng.');
  };

  const handleDiscardChanges = () => {
    setStatusMessage(null);
    setErrorMessage(null);
    const baseline = policyQuery.data
      ? extractFormValues(policyQuery.data)
      : DEFAULT_QUEUE_POLICY_VALUES;
    reset(baseline);
  };

  const actionsDisabled = policyQuery.isPending || policyQuery.isError || updateMutation.isPending;

  return (
    <AdminPortalShell
      activeTab="Chính sách hàng đợi"
      showTabs={false}
      breadcrumbs={[
        { label: 'Cấu hình Cơ sở' },
        { label: 'Chính sách hàng đợi khám (SCR-ADM-POL-02)' },
      ]}
    >
      <form onSubmit={handleSubmit(handleSave)} noValidate>
        <main className="px-6 pb-28 pt-6">
          {/* Subheader bar */}
          <div className="flex flex-wrap items-center justify-between gap-3 text-xs">
            <div className="flex flex-wrap items-center gap-3">
              <span className="rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-[11px] font-semibold text-primary">
                ● Cấu hình theo cơ sở (UC55)
              </span>
              <span className="font-mono text-[11px] text-sub">Mã màn hình: SCR-ADM-POL-02</span>
            </div>

            <div className="flex items-center gap-2 text-xs text-sub">
              <Clock className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
              <span>{isDirty ? 'Thay đổi chưa lưu' : 'Đồng bộ với cấu hình hiện hành'}</span>
            </div>
          </div>

          {/* Heading */}
          <div className="mt-4">
            <h1 className="text-2xl font-bold tracking-tight text-ink">
              Cấu hình Chính sách Hàng đợi Khám
            </h1>
            <p className="mt-1 text-xs text-sub">
              Thiết lập ân hạn đúng giờ, điều kiện Safe Backfill, tỷ lệ điều phối công bằng và các
              quy tắc bảo vệ số thứ tự / xác minh ưu tiên cho hàng đợi khám ngoại trú.
            </p>
          </div>

          {/* Operational Guidance Banner */}
          <div className="my-5 flex items-start gap-3 rounded-lg border border-sky-200 bg-sky-50/70 p-4 text-xs leading-relaxed text-sky-900 shadow-xs">
            <span className="mt-0.5 grid h-6 w-6 shrink-0 place-items-center rounded-full bg-white text-sky-600 shadow-2xs">
              <Info className="h-4 w-4" aria-hidden="true" />
            </span>
            <p>
              <span className="font-bold">Lưu ý vận hành Chính sách Hàng đợi:</span> Các tham số tại
              đây được Backend dùng để tính thứ tự phục vụ (servingOrder) và chọn bệnh nhân kế tiếp.
              Lưu cấu hình tạo phiên bản mới trong{' '}
              <code className="font-mono">hospital_policies</code> và ghi nhật ký kiểm toán.
            </p>
          </div>

          {isSystemAdminOnly && (
            <Alert tone="info" className="mb-5">
              <div className="font-bold">Tài khoản Quản trị hệ thống (SYSTEM_ADMIN)</div>
              <div className="mt-0.5">
                Chính sách hàng đợi được cấu hình riêng theo từng bệnh viện bởi Quản trị viên cơ sở
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
              <span className="mt-3 font-medium">Đang tải cấu hình chính sách hàng đợi...</span>
            </div>
          ) : policyQuery.isError ? (
            <div role="alert" className="mb-5">
              <Alert tone="error">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div>
                    <div className="font-bold">Không thể tải cấu hình chính sách hàng đợi</div>
                    <div className="mt-0.5">
                      {getQueuePolicyErrorMessage(
                        policyQuery.error,
                        'Không thể tải cấu hình chính sách hàng đợi của cơ sở.',
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
              <QueuePolicyForm
                register={register}
                errors={errors}
                disabled={updateMutation.isPending}
              />

              <QueuePolicySidebar
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
              <span>Thay đổi có hiệu lực cho các lượt điều phối hàng đợi tiếp theo.</span>
            </div>

            <div className="flex flex-wrap items-center gap-2.5">
              <Button
                type="button"
                variant="secondary"
                size="sm"
                onClick={handleRestoreDefaults}
                disabled={actionsDisabled}
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
                disabled={!isDirty || actionsDisabled}
                className="gap-1.5"
              >
                <Undo2 className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Hoàn tác thay đổi</span>
              </Button>
              <Button
                id="queue-policy-submit"
                type="submit"
                size="sm"
                loading={updateMutation.isPending}
                disabled={actionsDisabled}
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
