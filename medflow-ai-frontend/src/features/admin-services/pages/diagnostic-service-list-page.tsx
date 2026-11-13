import { FlaskConical, Plus, RefreshCw } from 'lucide-react';
import { useMemo, useState } from 'react';

import { useRoleAccess } from '@/features/auth/hooks/use-role-access';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';

import { AdminServicesShell } from '../components/admin-services-shell';
import { DiagnosticServiceDeactivateModal } from '../components/diagnostic-service-deactivate-modal';
import { DiagnosticServiceFilters } from '../components/diagnostic-service-filters';
import { DiagnosticServiceFormModal } from '../components/diagnostic-service-form-modal';
import { DiagnosticServiceMetrics } from '../components/diagnostic-service-metrics';
import { DiagnosticServiceTable } from '../components/diagnostic-service-table';
import { ServicePagination } from '../components/service-pagination';
import {
  useDiagnosticServiceDepartmentOptions,
  useDiagnosticServiceList,
} from '../hooks/use-diagnostic-services';
import type {
  DiagnosticServiceFilterValue,
  DiagnosticServiceItem,
  DiagnosticServiceListParams,
} from '../types/diagnostic-service.types';

export function DiagnosticServiceListPage({
  explicitPermissions,
}: { explicitPermissions?: readonly string[] | null } = {}) {
  const { hasPermission } = useRoleAccess(explicitPermissions);
  const canManage = hasPermission('HOSPITAL_CONFIG');
  const [filters, setFilters] = useState<DiagnosticServiceFilterValue>({ keyword: '' });
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [formState, setFormState] = useState<{
    mode: 'create' | 'edit';
    service: DiagnosticServiceItem | null;
  } | null>(null);
  const [deactivateTarget, setDeactivateTarget] = useState<DiagnosticServiceItem | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const params = useMemo<DiagnosticServiceListParams>(
    () => ({
      keyword: filters.keyword || undefined,
      departmentId: filters.departmentId,
      status: filters.status,
      requiresResultVerification: filters.requiresResultVerification,
      page,
      pageSize,
    }),
    [
      filters.keyword,
      filters.departmentId,
      filters.status,
      filters.requiresResultVerification,
      page,
      pageSize,
    ],
  );

  const { data, isPending, isError, error, refetch, isFetching } = useDiagnosticServiceList(params);
  const { data: departmentOptions = [], isPending: isDepartmentOptionsLoading } =
    useDiagnosticServiceDepartmentOptions();

  const hasActiveFilters = Boolean(
    filters.keyword ||
    filters.departmentId !== undefined ||
    filters.status !== undefined ||
    filters.requiresResultVerification !== undefined,
  );

  const handleFilterChange = (next: DiagnosticServiceFilterValue) => {
    setFilters(next);
    setPage(1);
  };

  const handleResetFilters = () => {
    setFilters({ keyword: '' });
    setPage(1);
  };

  return (
    <AdminServicesShell variant="diagnostic">
      <main className="space-y-5 px-6 py-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h1 className="text-lg font-bold text-ink">Danh mục Dịch vụ Cận lâm sàng</h1>
            {data && (
              <p className="mt-1 text-xs text-sub">
                <span>{data.hospitalName}</span>
                {' · '}
                <span className="font-mono font-medium text-navy">{data.hospitalCode}</span>
              </p>
            )}
          </div>
          {canManage && (
            <Button type="button" onClick={() => setFormState({ mode: 'create', service: null })}>
              <Plus aria-hidden="true" className="h-4 w-4" />
              Thêm dịch vụ
            </Button>
          )}
        </div>

        {isPending && (
          <div
            role="status"
            className="rounded-2xl border border-border bg-white p-6 text-center text-xs font-medium text-sub"
          >
            Đang tải danh mục dịch vụ cận lâm sàng…
          </div>
        )}

        {isError && (
          <Alert tone="error">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <span>
                {getErrorMessage(
                  error,
                  'Không thể tải danh mục dịch vụ cận lâm sàng. Vui lòng thử lại.',
                )}
              </span>
              <button
                type="button"
                onClick={() => void refetch()}
                className="inline-flex items-center gap-1 rounded-lg border border-rose-300 bg-white px-3 py-1 text-xs font-semibold text-rose-700 transition-colors hover:bg-rose-50"
              >
                <RefreshCw aria-hidden="true" className="h-3.5 w-3.5" />
                Thử lại
              </button>
            </div>
          </Alert>
        )}

        {data && !isPending && !isError && (
          <>
            <DiagnosticServiceMetrics metrics={data.metrics} />

            <DiagnosticServiceFilters
              value={filters}
              departmentOptions={departmentOptions}
              isDepartmentOptionsLoading={isDepartmentOptionsLoading}
              onChange={handleFilterChange}
              onReset={handleResetFilters}
            />

            {notice && <Alert tone="success">{notice}</Alert>}

            <section
              aria-label="Bảng danh mục dịch vụ cận lâm sàng"
              aria-busy={isFetching}
              className="overflow-hidden rounded-xl border border-border bg-white shadow-sm"
            >
              {data.data.length === 0 ? (
                <div className="flex flex-col items-center justify-center px-6 py-12 text-center">
                  <span className="grid h-12 w-12 place-items-center rounded-full bg-canvas text-sub">
                    <FlaskConical aria-hidden="true" className="h-6 w-6" />
                  </span>
                  {hasActiveFilters ? (
                    <>
                      <p className="mt-3 text-sm font-semibold text-ink">
                        Không tìm thấy dịch vụ cận lâm sàng phù hợp bộ lọc.
                      </p>
                      <Button
                        type="button"
                        variant="secondary"
                        size="sm"
                        onClick={handleResetFilters}
                        className="mt-3"
                      >
                        Xoá bộ lọc
                      </Button>
                    </>
                  ) : (
                    <p className="mt-3 text-sm font-semibold text-ink">
                      Cơ sở chưa có dịch vụ cận lâm sàng nào.
                    </p>
                  )}
                </div>
              ) : (
                <>
                  <DiagnosticServiceTable
                    services={data.data}
                    canManage={canManage}
                    onEdit={(service) => setFormState({ mode: 'edit', service })}
                    onDeactivate={setDeactivateTarget}
                  />
                  <ServicePagination
                    count={data.total}
                    currentPage={data.page}
                    pageSize={data.pageSize}
                    totalPages={Math.max(1, data.totalPages)}
                    onPageChange={setPage}
                    onPageSizeChange={(nextSize) => {
                      setPageSize(nextSize);
                      setPage(1);
                    }}
                  />
                </>
              )}
            </section>
          </>
        )}

        <DiagnosticServiceFormModal
          open={formState !== null}
          mode={formState?.mode ?? 'create'}
          service={formState?.service ?? null}
          onClose={() => setFormState(null)}
          onSaved={(item) => {
            setNotice(
              formState?.mode === 'edit'
                ? `Đã cập nhật dịch vụ ${item.serviceCode}.`
                : `Đã tạo dịch vụ ${item.serviceCode}.`,
            );
            setFormState(null);
          }}
        />
        <DiagnosticServiceDeactivateModal
          open={deactivateTarget !== null}
          service={deactivateTarget}
          onClose={() => setDeactivateTarget(null)}
          onDeactivated={(item) => {
            setNotice(`Đã ngừng hoạt động dịch vụ ${item.serviceCode}.`);
            setDeactivateTarget(null);
          }}
        />
      </main>
    </AdminServicesShell>
  );
}
