import { useEffect, useMemo, useRef } from 'react';

import { AdminReportsShell } from '../components/admin-reports-shell';
import { ReportFilters } from '../components/report-filters';
import { ReportMetrics } from '../components/report-metrics';
import { ReportOperationalCharts } from '../components/report-operational-charts';
import { ReportPagination } from '../components/report-pagination';
import { ReportTable } from '../components/report-table';
import { useOperationalReportStore } from '../store/operational-report.store';
import type { OperationalReportItem } from '../types/operational-report.types';

function exportOperationalReportsToCsv(reports: OperationalReportItem[]) {
  const quote = (val: string) => `"${val.replaceAll('"', '""').replace(/^[=+\-@]/, "'$&")}"`;
  const headers = [
    'Mã BC',
    'Tên báo cáo',
    'Kỳ tổng hợp',
    'Phân nhóm',
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

/** SCR-ADM-RPT-01 · Báo cáo vận hành — Hospital Admin Portal operational analytics. */
export function OperationalReportsPage() {
  const searchInputRef = useRef<HTMLInputElement>(null);

  const reports = useOperationalReportStore((state) => state.reports);
  const searchQuery = useOperationalReportStore((state) => state.searchQuery);
  const periodFilter = useOperationalReportStore((state) => state.periodFilter);
  const facilityFilter = useOperationalReportStore((state) => state.facilityFilter);
  const categoryFilter = useOperationalReportStore((state) => state.categoryFilter);
  const page = useOperationalReportStore((state) => state.page);
  const pageSize = useOperationalReportStore((state) => state.pageSize);
  const selectedIds = useOperationalReportStore((state) => state.selectedIds);

  const setPage = useOperationalReportStore((state) => state.setPage);
  const setPageSize = useOperationalReportStore((state) => state.setPageSize);
  const toggleSelection = useOperationalReportStore((state) => state.toggleSelection);
  const toggleAll = useOperationalReportStore((state) => state.toggleAll);
  const refreshReport = useOperationalReportStore((state) => state.refreshReport);

  const filteredReports = useMemo(() => {
    const normalizedQuery = searchQuery.trim().toLowerCase();

    return reports.filter((report) => {
      const matchesQuery =
        !normalizedQuery ||
        report.code.toLowerCase().includes(normalizedQuery) ||
        report.name.toLowerCase().includes(normalizedQuery) ||
        report.periodLabel.toLowerCase().includes(normalizedQuery) ||
        report.category.toLowerCase().includes(normalizedQuery);

      const matchesPeriod =
        !periodFilter ||
        periodFilter === '22/10/2026' ||
        periodFilter === 'Tất cả kỳ báo cáo' ||
        report.periodLabel === periodFilter;

      const matchesFacility = !facilityFilter || report.facility === facilityFilter;
      const matchesCategory = !categoryFilter || report.category === categoryFilter;

      return matchesQuery && matchesPeriod && matchesFacility && matchesCategory;
    });
  }, [reports, searchQuery, periodFilter, facilityFilter, categoryFilter]);

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

  const totalPages = Math.max(1, Math.ceil(filteredReports.length / pageSize));
  const currentPage = Math.min(page, totalPages);
  const paginatedReports = filteredReports.slice(
    (currentPage - 1) * pageSize,
    currentPage * pageSize,
  );

  return (
    <AdminReportsShell>
      <main className="mx-auto max-w-[1720px] space-y-6 px-6 py-6">
        <ReportMetrics reports={reports} />

        <ReportFilters
          searchRef={searchInputRef}
          filteredReports={filteredReports}
          onExportCsv={exportOperationalReportsToCsv}
        />

        <ReportOperationalCharts />

        <section
          className="overflow-hidden rounded-lg border border-border bg-white shadow-sm"
          aria-label="Bảng danh sách báo cáo vận hành"
        >
          <ReportTable
            reports={paginatedReports}
            selectedIds={selectedIds}
            onToggleSelection={toggleSelection}
            onToggleAll={toggleAll}
            onRefreshReport={refreshReport}
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
      </main>
    </AdminReportsShell>
  );
}
