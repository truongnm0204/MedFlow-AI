import { CheckCircle2 } from 'lucide-react';
import { useMemo, useState } from 'react';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { toast } from '@/shared/ui/toast';

import { deactivateDepartmentApi, reactivateDepartmentApi } from '../api/department.api';
import { DepartmentDeactivateModal } from '../components/department-deactivate-modal';
import { DepartmentFilters } from '../components/department-filters';
import { DepartmentMetricsCards } from '../components/department-metrics';
import { DepartmentTable } from '../components/department-table';
import { useDepartmentStore } from '../store/department.store';
import type { DeactivateDepartmentPayload } from '../types/department-deactivate.types';
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
  const departments = useDepartmentStore((state) => state.departments);
  const [filters, setFilters] = useState<Filters>({ search: '', facility: '', status: '' });
  const [page, setPage] = useState(1);
  const [deactivatingDepartment, setDeactivatingDepartment] = useState<DepartmentItem | null>(null);
  const [feedbackMessage, setFeedbackMessage] = useState<string | null>(null);
  const [feedbackType, setFeedbackType] = useState<'success' | 'error'>('success');

  const showFeedback = (message: string, type: 'success' | 'error' = 'success') => {
    setFeedbackMessage(message);
    setFeedbackType(type);
    if (type === 'success') {
      toast.success(message);
    } else {
      toast.error(message);
    }
    setTimeout(() => setFeedbackMessage(null), 4000);
  };

  const handleConfirmDeactivate = async (
    departmentId: string,
    payload: DeactivateDepartmentPayload,
  ) => {
    const target = departments.find((d) => d.id === departmentId || d.code === departmentId);
    try {
      const res = await deactivateDepartmentApi(departmentId, payload);
      showFeedback(res.message);
      setDeactivatingDepartment(null);
    } catch (error: unknown) {
      const errMessage =
        error instanceof Error
          ? error.message
          : `Không thể ngừng hoạt động khoa ${target?.name ?? departmentId}.`;
      showFeedback(errMessage, 'error');
    }
  };

  const handleToggleStatus = async (departmentId: string) => {
    const target = departments.find((d) => d.id === departmentId || d.code === departmentId);
    if (!target) return;

    if (target.status === 'ACTIVE') {
      setDeactivatingDepartment(target);
    } else {
      try {
        const res = await reactivateDepartmentApi(departmentId);
        showFeedback(res.message);
      } catch (error: unknown) {
        const errMessage =
          error instanceof Error ? error.message : `Không thể kích hoạt lại khoa ${target.name}.`;
        showFeedback(errMessage, 'error');
      }
    }
  };
  const metrics = useMemo<DepartmentMetrics>(() => {
    const active = departments.filter((d) => d.status === 'ACTIVE').length;
    return {
      total: departments.length,
      active,
      inactive: departments.length - active,
      facilities: new Set(departments.map((d) => d.facility)).size,
    };
  }, [departments]);

  const facilities = useMemo(() => [...new Set(departments.map((d) => d.facility))], [departments]);

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
          onRequestDeactivate={(dept) => setDeactivatingDepartment(dept)}
          onToggleStatus={handleToggleStatus}
        />
      </main>
      <DepartmentDeactivateModal
        department={deactivatingDepartment}
        open={Boolean(deactivatingDepartment)}
        onClose={() => setDeactivatingDepartment(null)}
        onConfirm={handleConfirmDeactivate}
      />
      {feedbackMessage && (
        <div
          role="status"
          aria-live="polite"
          className={`fixed bottom-6 right-6 z-50 flex items-center gap-2.5 rounded-xl border px-4 py-3 text-xs font-semibold shadow-lg transition-all ${
            feedbackType === 'error'
              ? 'border-rose-200 bg-rose-50 text-rose-800'
              : 'border-emerald-200 bg-emerald-50 text-emerald-800'
          }`}
        >
          <CheckCircle2
            className={`h-4 w-4 shrink-0 ${
              feedbackType === 'error' ? 'text-rose-600' : 'text-emerald-600'
            }`}
            aria-hidden="true"
          />
          <span>{feedbackMessage}</span>
        </div>
      )}
    </AdminPortalShell>
  );
}
