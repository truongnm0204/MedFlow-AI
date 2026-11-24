import { RefreshCw } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';

import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';

import { AdminReportsShell } from '../components/admin-reports-shell';
import { ReportFilters } from '../components/report-filters';
import { ReportMetrics } from '../components/report-metrics';
import { ReportOperationalCharts } from '../components/report-operational-charts';
import { ReportPagination } from '../components/report-pagination';
import { ReportTable } from '../components/report-table';
import { useOperationalReport } from '../hooks/use-operational-report';
import type { OperationalReportItem } from '../types/operational-report.types';

function exportOperationalReportsToCsv(reports: OperationalReportItem[]) {
  const quote = (val: string) => `"${val.replaceAll('"', '""').replace(/^[=+\-@]/, "'$&")}"`;
  const headers = [
    'Mã BC',
    'Tên báo cáo',
    'Kỳ tổng hợp',
    'Khoa phòng',
    'Cơ sở',
    'Lượt tiếp nhận',
    'Chờ trung vị',
    'Trạng thái tổng hợp',
    'Trạng thái vận hành',
  ];
  const rows = reports.map((report) => [
    report.code,
    report.name,
    report.periodLabel,
    report.category,
    report.facility,
    report.intakeVolumeLabel,
    report.medianWaitLabel,
    report.aggregationStatus === 'AGGREGATED' ? 'Đã tổng hợp' : 'Cần làm mới',
    report.status === 'COMPLETED'
      ? 'Hoàn tất'
      : report.status === 'NEEDS_REFRESH'
        ? 'Cần làm mới'
        : 'Cần rà soát',
  ]);

  const csv = '\uFEFF' + [headers, ...rows].map((row) => row.map(quote).join(',')).join('\r\n');
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = 'bao-cao-van-hanh-medflow.csv';
  link.click();
  URL.revokeObjectURL(url);
}

/** SCR-ADM-RPT-01 · Báo cáo vận hành — Hospital Admin Portal operational analytics (UC56). */
export function OperationalReportsPage() {
  const searchInputRef = useRef<HTMLInputElement>(null);

  const [searchQuery, setSearchQuery] = useState('');
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [departmentId, setDepartmentId] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);

  const reportQuery = useOperationalReport({
    fromDate: fromDate || undefined,
    toDate: toDate || undefined,
    departmentId: departmentId ? Number(departmentId) : undefined,
  });

  const data = reportQuery.data;
  const reports = useMemo(() => data?.reports ?? [], [data]);

  const filteredReports = useMemo(() => {
    const normalizedQuery = searchQuery.trim().toLowerCase();
    if (!normalizedQuery) return reports;

    return reports.filter(
      (report) =>
        report.code.toLowerCase().includes(normalizedQuery) ||
        report.name.toLowerCase().includes(normalizedQuery) ||
        report.periodLabel.toLowerCase().includes(normalizedQuery) ||
        report.category.toLowerCase().includes(normalizedQuery),
    );
  }, [reports, searchQuery]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        searchInputRef.current?.focus();
      } else if (event.key === 'F2') {
        event.preventDefault();
        exportOperationalReportsToCsv(filteredReports);
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [filteredReports]);

  const hasActiveFilters =
    Boolean(searchQuery) || Boolean(fromDate) || Boolean(toDate) || Boolean(departmentId);

  const handleResetFilters = () => {
    setSearchQuery('');
    setFromDate('');
    setToDate('');
    setDepartmentId('');
    setPage(1);
  };

  const toggleSelection = (id: string) => {
    setSelectedIds((prev) =>
      prev.includes(id) ? prev.filter((item) => item !== id) : [...prev, id],
    );
  };

  const toggleAll = (visibleIds: string[]) => {
    setSelectedIds((prev) => {
      const allSelected = visibleIds.length > 0 && visibleIds.every((id) => prev.includes(id));
      return allSelected
        ? prev.filter((id) => !visibleIds.includes(id))
        : [...new Set([...prev, ...visibleIds])];
    });
  };

  const totalPages = Math.max(1, Math.ceil(filteredReports.length / pageSize));
  const currentPage = Math.min(page, totalPages);
  const paginatedReports = filteredReports.slice(
    (currentPage - 1) * pageSize,
    currentPage * pageSize,
  );

  return (
    <AdminReportsShell>
      <main className="mx-auto max-w-[1720px] space-y-6 px-6 py-6">
        <ReportFilters
          searchRef={searchInputRef}
          searchQuery={searchQuery}
          onSearchChange={(val) => {
            setSearchQuery(val);
            setPage(1);
          }}
          fromDate={fromDate}
          toDate={toDate}
          onFromDateChange={(val) => {
            setFromDate(val);
            setPage(1);
          }}
          onToDateChange={(val) => {
            setToDate(val);
            setPage(1);
          }}
          departmentId={departmentId}
          onDepartmentChange={(val) => {
            setDepartmentId(val);
            setPage(1);
          }}
          departments={data?.availableDepartments ?? []}
          hasActiveFilters={hasActiveFilters}
          onResetFilters={handleResetFilters}
          onRefresh={() => void reportQuery.refetch()}
          filteredReports={filteredReports}
          onExportCsv={exportOperationalReportsToCsv}
        />

        {reportQuery.isPending && (
          <div
            role="status"
            className="rounded-lg border border-border bg-white p-10 text-center text-xs text-sub"
          >
            Đang tổng hợp dữ liệu báo cáo vận hành từ hệ thống...
          </div>
        )}

        {!reportQuery.isPending && reportQuery.isError && (
          <div role="alert">
            <Alert tone="error">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <span>
                  {reportQuery.error instanceof Error
                    ? reportQuery.error.message
                    : 'Không thể tải báo cáo vận hành.'}
                </span>
                <Button
                  type="button"
                  variant="secondary"
                  size="sm"
                  onClick={() => void reportQuery.refetch()}
                  className="gap-1.5"
                >
                  <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Thử lại</span>
                </Button>
              </div>
            </Alert>
          </div>
        )}

        {!reportQuery.isPending && data && (
          <>
            <ReportMetrics summary={data.summary} periodLabel={data.periodLabel} />

            <ReportOperationalCharts
              facilityName={data.facilityName}
              periodLabel={data.periodLabel}
              totalVisits={data.summary.totalVisits}
              hourlyIntake={data.hourlyIntake}
              departmentVisits={data.departmentVisits}
              visitStatusSegments={data.visitStatusSegments}
            />

            <section
              className="overflow-hidden rounded-lg border border-border bg-white shadow-sm"
              aria-label="Bảng danh sách báo cáo vận hành"
            >
              <ReportTable
                reports={paginatedReports}
                selectedIds={selectedIds}
                onToggleSelection={toggleSelection}
                onToggleAll={toggleAll}
                onRefreshReport={() => void reportQuery.refetch()}
              />
              <ReportPagination
                count={filteredReports.length}
                currentPage={currentPage}
                pageSize={pageSize}
                totalPages={totalPages}
                onPageChange={setPage}
                onPageSizeChange={setPageSize}
              />
            </section>
          </>
        )}
      </main>
    </AdminReportsShell>
  );
}
