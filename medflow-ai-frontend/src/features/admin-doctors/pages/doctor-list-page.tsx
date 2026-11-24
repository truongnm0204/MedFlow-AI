import { CheckCircle2, Download, Loader2, Search, SlidersHorizontal, UserPlus } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, Outlet, useNavigate } from 'react-router-dom';

import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';

import { AdminDoctorShell } from '../components/admin-doctor-shell';
import { DoctorDeactivateModal } from '../components/doctor-deactivate-modal';
import { DoctorMetrics } from '../components/doctor-metrics';
import { DoctorPagination } from '../components/doctor-pagination';
import { DoctorTable } from '../components/doctor-table';
import { DEGREES, DEPARTMENTS } from '../data/doctor-demo-data';
import {
  useAdminDoctors,
  useDeactivateAdminDoctorMutation,
  useReactivateAdminDoctorMutation,
} from '../hooks/use-admin-doctors';
import type { Doctor } from '../types/doctor.types';

const PAGE_SIZE = 8;

function downloadCsv(doctors: Doctor[]) {
  const quote = (value: string) => `"${value.replaceAll('"', '""').replace(/^[=+\-@]/, "'$&")}"`;
  const rows = [
    ['Mã BS', 'Họ tên', 'Email', 'Chuyên khoa', 'Khoa', 'CCHN', 'Trạng thái'],
    ...doctors.map((doctor) => [
      doctor.id,
      doctor.name,
      doctor.email,
      doctor.specialty,
      doctor.department,
      doctor.license,
      doctor.active ? 'Đang nhận lịch' : 'Tạm ngừng',
    ]),
  ];
  const csv = '\uFEFF' + rows.map((row) => row.map(quote).join(',')).join('\r\n');
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = 'danh-sach-bac-si.csv';
  link.click();
  URL.revokeObjectURL(url);
}

/** SCR-ADM-DOC-01 — administrative doctor list backed by GET /api/v1/admin/doctors. */
export function DoctorListPage() {
  const navigate = useNavigate();
  const { data: doctors = [], isLoading, isError, error, refetch } = useAdminDoctors();
  const deactivateMutation = useDeactivateAdminDoctorMutation();
  const reactivateMutation = useReactivateAdminDoctorMutation();

  const [query, setQuery] = useState('');
  const [department, setDepartment] = useState('');
  const [degree, setDegree] = useState('');
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(1);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [deactivatingDoctor, setDeactivatingDoctor] = useState<Doctor | null>(null);
  const [feedbackMessage, setFeedbackMessage] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const searchRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const handler = (event: KeyboardEvent) => {
      if (event.ctrlKey && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        searchRef.current?.focus();
      }
      if (event.key === 'F2') {
        event.preventDefault();
        navigate('/admin/doctors/new');
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [navigate]);

  const departmentOptions = useMemo(() => {
    const set = new Set<string>(DEPARTMENTS);
    for (const doc of doctors) {
      if (doc.department) set.add(doc.department);
    }
    return Array.from(set);
  }, [doctors]);

  const filtered = useMemo(
    () =>
      doctors.filter((doctor) => {
        const term = query.trim().toLocaleLowerCase('vi');
        const matches =
          !term ||
          [doctor.id, doctor.name, doctor.specialty, doctor.license].some((value) =>
            value.toLocaleLowerCase('vi').includes(term),
          );
        return (
          matches &&
          (!department || doctor.department === department) &&
          (!degree || doctor.degree === degree) &&
          (!status || String(doctor.active) === status)
        );
      }),
    [doctors, query, department, degree, status],
  );
  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const currentPage = Math.min(page, totalPages);
  const visible = filtered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE);
  const updateFilter = (setter: (value: string) => void, value: string) => {
    setter(value);
    setPage(1);
  };
  const toggleSelection = (id: string) =>
    setSelectedIds((ids) => (ids.includes(id) ? ids.filter((item) => item !== id) : [...ids, id]));
  const toggleAll = () =>
    setSelectedIds((ids) =>
      visible.every((doctor) => ids.includes(doctor.id))
        ? ids.filter((id) => !visible.some((doctor) => doctor.id === id))
        : [...new Set([...ids, ...visible.map((doctor) => doctor.id)])],
    );

  const handleConfirmDeactivate = async (doctorId: string) => {
    const doctor = doctors.find((item) => item.id === doctorId);
    if (!doctor) {
      setDeactivatingDoctor(null);
      return;
    }
    setActionError(null);
    try {
      await deactivateMutation.mutateAsync({ idOrCode: doctor.id });
      setFeedbackMessage(`Đã chuyển trạng thái bác sĩ ${doctor.name} sang Ngừng hoạt động.`);
      setTimeout(() => setFeedbackMessage(null), 4000);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Không thể ngừng hoạt động bác sĩ.');
    } finally {
      setDeactivatingDoctor(null);
    }
  };

  const handleToggleStatus = async (id: string) => {
    const doctor = doctors.find((item) => item.id === id);
    if (!doctor) return;
    if (doctor.active) {
      setDeactivatingDoctor(doctor);
      return;
    }

    setActionError(null);
    try {
      await reactivateMutation.mutateAsync(doctor.id);
      setFeedbackMessage(`Đã kích hoạt lại hoạt động cho bác sĩ ${doctor.name}.`);
      setTimeout(() => setFeedbackMessage(null), 4000);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Không thể kích hoạt lại bác sĩ.');
    }
  };

  return (
    <AdminDoctorShell showTabs>
      <main className="space-y-6 px-6 py-6">
        {isError ? (
          <Alert tone="error">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <span>
                <b>Không thể tải danh mục bác sĩ:</b>{' '}
                {error instanceof Error ? error.message : 'Đã xảy ra lỗi kết nối máy chủ.'}
              </span>
              <Button type="button" size="sm" variant="secondary" onClick={() => void refetch()}>
                Thử lại
              </Button>
            </div>
          </Alert>
        ) : null}

        {actionError ? <Alert tone="error">{actionError}</Alert> : null}

        <DoctorMetrics doctors={doctors} />
        <section
          className="space-y-3 rounded-lg border border-border bg-white p-4 shadow-sm"
          aria-label="Tìm kiếm và lọc bác sĩ"
        >
          <div className="flex flex-wrap items-center gap-3">
            <div className="flex min-w-80 flex-1 items-center gap-2.5 rounded-lg border border-border bg-canvas px-3.5 py-2.5 transition-colors focus-within:border-primary focus-within:bg-white focus-within:ring-1 focus-within:ring-primary">
              <Search className="h-4 w-4 text-slate-400" aria-hidden="true" />
              <input
                ref={searchRef}
                value={query}
                onChange={(event) => updateFilter(setQuery, event.target.value)}
                placeholder="Tìm theo mã BS, họ tên, chuyên khoa, CCHN..."
                className="min-w-0 flex-1 bg-transparent text-xs text-ink outline-none placeholder:text-slate-400"
                aria-label="Tìm bác sĩ"
              />
              <kbd className="hidden rounded border border-border bg-white px-1.5 py-0.5 text-[10px] font-medium text-sub sm:inline-block shadow-xs">
                Ctrl + K
              </kbd>
            </div>
            <Button
              variant="secondary"
              size="sm"
              onClick={() => searchRef.current?.focus()}
              className="gap-2"
            >
              <Search className="h-3.5 w-3.5 text-slate-500" aria-hidden="true" />
              Tìm kiếm
            </Button>
          </div>

          <div className="flex flex-wrap items-center justify-between gap-3 pt-1 border-t border-slate-100">
            <div className="flex flex-wrap items-center gap-2.5">
              <select
                aria-label="Lọc khoa phòng"
                className="rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink shadow-xs outline-none focus:border-primary"
                value={department}
                onChange={(event) => updateFilter(setDepartment, event.target.value)}
              >
                <option value="">Tất cả khoa phòng</option>
                {departmentOptions.map((item) => (
                  <option key={item}>{item}</option>
                ))}
              </select>
              <select
                aria-label="Lọc học hàm học vị"
                className="rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink shadow-xs outline-none focus:border-primary"
                value={degree}
                onChange={(event) => updateFilter(setDegree, event.target.value)}
              >
                <option value="">Tất cả học hàm/học vị</option>
                {DEGREES.map((item) => (
                  <option key={item}>{item}</option>
                ))}
              </select>
              <select
                aria-label="Lọc trạng thái"
                className="rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink shadow-xs outline-none focus:border-primary"
                value={status}
                onChange={(event) => updateFilter(setStatus, event.target.value)}
              >
                <option value="">Tất cả trạng thái</option>
                <option value="true">Đang hoạt động</option>
                <option value="false">Ngừng hoạt động</option>
              </select>
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <Button variant="secondary" size="sm" onClick={() => downloadCsv(filtered)}>
                <Download className="h-4 w-4" aria-hidden="true" />
                Xuất CSV
              </Button>
              <Button
                variant="secondary"
                size="sm"
                disabled
                aria-label="Tùy chọn cột"
                title="Tùy chọn cột sẽ có ở phiên bản sau"
              >
                <SlidersHorizontal className="h-4 w-4" aria-hidden="true" />
              </Button>
              <Link
                to="/admin/doctors/new"
                className="inline-flex items-center gap-2 rounded-lg bg-primary px-3.5 py-2 text-xs font-bold text-white shadow-sm hover:bg-primary-dark transition-colors"
              >
                <UserPlus className="h-4 w-4" aria-hidden="true" />
                Thêm Hồ sơ Bác sĩ{' '}
                <kbd className="rounded bg-white/20 px-1.5 py-0.5 text-[10px]">F2</kbd>
              </Link>
            </div>
          </div>
        </section>
        <section
          className="overflow-hidden rounded-lg border border-border bg-white shadow-sm"
          aria-label="Danh sách bác sĩ"
        >
          {isLoading ? (
            <div className="flex items-center justify-center gap-2 py-12 text-sm text-sub">
              <Loader2 className="h-4 w-4 animate-spin text-primary" aria-hidden="true" />
              <span>Đang tải danh sách bác sĩ...</span>
            </div>
          ) : (
            <>
              <DoctorTable
                doctors={visible}
                selectedIds={selectedIds}
                onToggleSelection={toggleSelection}
                onToggleAll={toggleAll}
                onToggleStatus={(id) => void handleToggleStatus(id)}
                onRequestDeactivate={(doc) => setDeactivatingDoctor(doc)}
              />
              <DoctorPagination
                count={filtered.length}
                currentPage={currentPage}
                pageSize={PAGE_SIZE}
                totalPages={totalPages}
                onPageChange={setPage}
              />
            </>
          )}
        </section>
      </main>
      <DoctorDeactivateModal
        doctor={deactivatingDoctor}
        open={Boolean(deactivatingDoctor)}
        onClose={() => setDeactivatingDoctor(null)}
        onConfirm={(doctorId) => void handleConfirmDeactivate(doctorId)}
      />
      {feedbackMessage && (
        <div
          role="status"
          aria-live="polite"
          className="fixed bottom-6 right-6 z-50 flex items-center gap-2.5 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-xs font-semibold text-emerald-800 shadow-lg animate-in fade-in"
        >
          <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600" aria-hidden="true" />
          <span>{feedbackMessage}</span>
        </div>
      )}
      <Outlet />
    </AdminDoctorShell>
  );
}
