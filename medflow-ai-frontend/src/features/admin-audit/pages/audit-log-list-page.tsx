import { RefreshCw, ShieldCheck } from 'lucide-react';
import { type FormEvent, useState } from 'react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert, Badge, Button, Spinner } from '@/shared/ui';

import { AuditLogDetailDrawer } from '../components/audit-log-detail-drawer';
import { AuditLogFilterBar } from '../components/audit-log-filters';
import { AuditLogMetricsCards } from '../components/audit-log-metrics';
import { AuditLogTable } from '../components/audit-log-table';
import { useAuditLogs } from '../hooks/use-audit-logs';
import {
  type AuditLogFilterFormInputs,
  validateAuditLogFilterInputs,
} from '../schemas/audit-log.schema';
import type {
  AuditLogFilterParams,
  AuditLogListItemDto,
  AuditLogMetricsDto,
} from '../types/audit-log.types';

const INITIAL_INPUTS: AuditLogFilterFormInputs = {
  keyword: '',
  action: '',
  entityType: '',
  actorUserIdInput: '',
  hospitalIdInput: '',
  fromDate: '',
  toDate: '',
};

const EMPTY_METRICS: AuditLogMetricsDto = {
  totalLogs: 0,
  last24HoursCount: 0,
  authEventsCount: 0,
  configAndRbacEventsCount: 0,
};

export function AuditLogListPage() {
  const user = useAuthStore((s) => s.user);
  const isSystemAdmin = Boolean(user?.roles.includes('SYSTEM_ADMIN'));

  const [inputs, setInputs] = useState<AuditLogFilterFormInputs>(INITIAL_INPUTS);
  const [appliedParams, setAppliedParams] = useState<AuditLogFilterParams>({
    page: 1,
    pageSize: 20,
  });
  const [validationError, setValidationError] = useState<string | null>(null);
  const [selectedItem, setSelectedItem] = useState<AuditLogListItemDto | null>(null);

  const logsQuery = useAuditLogs(appliedParams, true);

  const handleInputChange = (patch: Partial<AuditLogFilterFormInputs>) => {
    setInputs((prev) => ({ ...prev, ...patch }));
  };

  const handleApplyFilters = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const result = validateAuditLogFilterInputs(inputs);
    if (!result.isValid || !result.params) {
      setValidationError(result.errorMessage ?? 'Bộ lọc nhật ký kiểm toán không hợp lệ.');
      return;
    }

    setValidationError(null);
    setAppliedParams((prev) => ({
      ...result.params,
      page: 1,
      pageSize: prev.pageSize ?? 20,
    }));
  };

  const handleResetFilters = () => {
    setInputs(INITIAL_INPUTS);
    setValidationError(null);
    setAppliedParams((prev) => ({
      page: 1,
      pageSize: prev.pageSize ?? 20,
    }));
  };

  const handlePageChange = (nextPage: number) => {
    setAppliedParams((prev) => ({ ...prev, page: Math.max(1, nextPage) }));
  };

  const handlePageSizeChange = (nextPageSize: number) => {
    setAppliedParams((prev) => ({ ...prev, page: 1, pageSize: nextPageSize }));
  };

  const items = logsQuery.data?.items ?? [];
  const metrics = logsQuery.data?.metrics ?? EMPTY_METRICS;

  return (
    <AdminPortalShell
      activeTab="Nhật ký kiểm toán"
      breadcrumbs={[
        {
          label: isSystemAdmin ? 'Quản trị Hệ thống' : 'Cấu hình Cơ sở',
          href: isSystemAdmin ? '/system-admin' : '/hospital-admin',
        },
        { label: 'Nhật ký kiểm toán (UC60)' },
      ]}
    >
      <main className="mx-auto max-w-[1680px] space-y-6 p-6">
        {/* Header Section */}
        <div className="flex flex-col justify-between gap-4 rounded-2xl border border-border bg-white p-5 shadow-sm sm:flex-row sm:items-center">
          <div className="space-y-1">
            <div className="flex flex-wrap items-center gap-2">
              <Badge tone="sky">
                <ShieldCheck className="mr-1 h-3.5 w-3.5" aria-hidden="true" />
                Kiểm toán bảo mật &amp; Vận hành (UC60)
              </Badge>
              <Badge tone="emerald">
                {isSystemAdmin ? 'Phạm vi: Toàn hệ thống' : 'Phạm vi: Cơ sở được phân công'}
              </Badge>
              <span className="font-mono text-[11px] text-sub">Mã màn hình: SCR-ADM-AUD-01</span>
            </div>
            <h1 className="text-xl font-bold tracking-tight text-ink sm:text-2xl">
              Nhật ký Kiểm toán Hệ thống (Audit Log)
            </h1>
            <p className="text-xs text-sub">
              Tra cứu lịch sử xác thực, phân quyền, thay đổi cấu hình chính sách và hoạt động nghiệp
              vụ theo múi giờ bệnh viện (UTC+7). Các trường nhạy cảm được tự động che bảo mật.
            </p>
          </div>

          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={() => void logsQuery.refetch()}
            disabled={logsQuery.isFetching}
          >
            <RefreshCw
              className={`h-3.5 w-3.5 ${logsQuery.isFetching ? 'animate-spin' : ''}`}
              aria-hidden="true"
            />
            <span>Làm mới dữ liệu</span>
          </Button>
        </div>

        <AuditLogMetricsCards metrics={metrics} />

        <AuditLogFilterBar
          inputs={inputs}
          availableActions={logsQuery.data?.availableActions ?? []}
          availableEntityTypes={logsQuery.data?.availableEntityTypes ?? []}
          availableHospitals={logsQuery.data?.availableHospitals ?? []}
          isSystemAdmin={isSystemAdmin}
          validationError={validationError}
          onInputChange={handleInputChange}
          onSubmit={handleApplyFilters}
          onReset={handleResetFilters}
        />

        {logsQuery.isPending ? (
          <div
            role="status"
            className="flex flex-col items-center justify-center gap-3 rounded-2xl border border-border bg-white py-16 text-xs text-sub shadow-sm"
          >
            <Spinner className="h-6 w-6 text-primary" />
            <span className="font-medium">Đang tải danh sách nhật ký kiểm toán...</span>
          </div>
        ) : logsQuery.isError ? (
          <div role="alert">
            <Alert tone="error">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div>
                  <div className="font-bold">Không thể tải nhật ký kiểm toán</div>
                  <div className="mt-0.5">
                    {getErrorMessage(
                      logsQuery.error,
                      'Không thể kết nối máy chủ hoặc tài khoản chưa được cấp quyền AUDIT_READ.',
                    )}
                  </div>
                </div>
                <Button
                  type="button"
                  variant="secondary"
                  size="sm"
                  onClick={() => void logsQuery.refetch()}
                >
                  Thử lại
                </Button>
              </div>
            </Alert>
          </div>
        ) : (
          <AuditLogTable
            items={items}
            total={logsQuery.data?.total ?? 0}
            page={logsQuery.data?.page ?? appliedParams.page ?? 1}
            pageSize={logsQuery.data?.pageSize ?? appliedParams.pageSize ?? 20}
            totalPages={logsQuery.data?.totalPages ?? 1}
            selectedLogId={selectedItem?.id ?? null}
            onSelectLog={setSelectedItem}
            onPageChange={handlePageChange}
            onPageSizeChange={handlePageSizeChange}
            onResetFilters={handleResetFilters}
          />
        )}

        <AuditLogDetailDrawer selectedItem={selectedItem} onClose={() => setSelectedItem(null)} />
      </main>
    </AdminPortalShell>
  );
}
