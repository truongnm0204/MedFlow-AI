import { useMemo, useState } from 'react';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

import { DepartmentFilters } from '../components/department-filters';
import { DepartmentMetricsCards } from '../components/department-metrics';
import { DepartmentTable } from '../components/department-table';
import { DEPARTMENT_MOCK_DATA } from '../data/department-mock-data';
import type {
  DepartmentFilters as Filters,
  DepartmentItem,
  DepartmentMetrics,
} from '../types/department.types';

const PAGE_SIZE = 10;

const quote = (val: string) => `"${val.replaceAll('"', '""').replace(/^[=+\-@]/, "'$&")}"`;

function exportDepartmentsToCsv(departments: DepartmentItem[]) {
  const headers = ['Mã Khoa', 'Tên Khoa', 'Cơ Sở', 'Trạng Thái'];
  const rows = departments.map((d) => [
    d.code,
    d.name,
    d.facility,
    d.status === 'ACTIVE' ? 'Đang hoạt động' : 'Tạm ngừng',
  ]);
  const csv = '\uFEFF' + [headers, ...rows].map((r) => r.map(quote).join(',')).join('\r\n');
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8;' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = 'danh-muc-khoa-phong-medflow.csv';
  link.click();
  URL.revokeObjectURL(url);
}

export function DepartmentListPage() {
  const departments = DEPARTMENT_MOCK_DATA;
  const [filters, setFilters] = useState<Filters>({ search: '', facility: '', status: '' });
  const [page, setPage] = useState(1);

  const metrics = useMemo<DepartmentMetrics>(() => {
    const active = departments.filter((d) => d.status === 'ACTIVE').length;
    return {
      total: departments.length,
      active,
      inactive: departments.length - active,
      facilities: new Set(departments.map((d) => d.facility)).size,
    };
  }, [departments]);

  const facilities = useMemo(
    () => [...new Set(departments.map((d) => d.facility))],
    [departments],
  );

  const filtered = useMemo(() => {
    const term = filters.search.trim().toLowerCase();
    return departments.filter(
      (d) =>
        (!term || d.code.toLowerCase().includes(term) || d.name.toLowerCase().includes(term)) &&
        (!filters.facility || d.facility === filters.facility) &&
        (!filters.status || d.status === filters.status),
    );
  }, [departments, filters]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const currentPage = Math.min(page, totalPages);
  const pageItems = filtered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE);

  return (
    <AdminPortalShell
      activeTab="Khoa phòng"
      breadcrumbText="Danh mục Khoa phòng (SCR-ADM-DEP-01)"
      breadcrumbLink="/admin/departments"
    >
      <main className="mx-auto max-w-[1720px] space-y-5 px-6 py-6">
        <DepartmentMetricsCards metrics={metrics} />
        <DepartmentFilters
          filters={filters}
          facilities={facilities}
          onChange={(next) => {
            setFilters(next);
            setPage(1);
          }}
          onExport={() => exportDepartmentsToCsv(filtered)}
        />
        <DepartmentTable
          departments={pageItems}
          totalCount={filtered.length}
          page={currentPage}
          pageSize={PAGE_SIZE}
          onPageChange={setPage}
        />
      </main>
    </AdminPortalShell>
  );
}
