import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, Outlet, useNavigate } from 'react-router-dom';

import { Button } from '@/shared/ui/button';

import { AdminDoctorShell } from '../components/admin-doctor-shell';
import { DoctorMetrics } from '../components/doctor-metrics';
import { DoctorPagination } from '../components/doctor-pagination';
import { DoctorTable } from '../components/doctor-table';
import { DEGREES, DEPARTMENTS } from '../data/doctor-demo-data';
import { useDoctorDemoStore } from '../store/doctor-demo.store';
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
  link.download = 'danh-sach-bac-si-minh-hoa.csv';
  link.click();
  URL.revokeObjectURL(url);
}

/** SCR-ADM-DOC-01 — synthetic, session-only administrative doctor list. */
export function DoctorListPage() {
  const navigate = useNavigate();
  const doctors = useDoctorDemoStore((state) => state.doctors);
  const updateDoctor = useDoctorDemoStore((state) => state.updateDoctor);
  const [query, setQuery] = useState('');
  const [department, setDepartment] = useState('');
  const [degree, setDegree] = useState('');
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(1);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
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

  return (
    <AdminDoctorShell showTabs>
      <main className="mx-auto max-w-[1720px] space-y-6 px-6 py-6">
        <DoctorMetrics doctors={doctors} />
        <section
          className="flex flex-wrap items-center gap-3 rounded-lg border border-border bg-white p-4 shadow-sm"
          aria-label="Tìm kiếm và lọc bác sĩ"
        >
          <div className="flex min-w-64 flex-1 items-center gap-2 rounded border border-border bg-canvas px-3 py-2.5">
            <img src="/assets/admin-doctors/list-14.svg" alt="" />
            <input
              ref={searchRef}
              value={query}
              onChange={(event) => updateFilter(setQuery, event.target.value)}
              placeholder="Tìm theo mã BS, họ tên, chuyên khoa, CCHN..."
              className="min-w-0 flex-1 bg-transparent text-xs outline-none placeholder:text-slate-400"
              aria-label="Tìm bác sĩ"
            />
            <kbd className="hidden rounded border border-border bg-white px-1.5 text-[10px] text-sub sm:block">
              Ctrl + K
            </kbd>
          </div>
          <select
            aria-label="Lọc khoa phòng"
            className="rounded border border-border bg-white px-3 py-2.5 text-xs"
            value={department}
            onChange={(event) => updateFilter(setDepartment, event.target.value)}
          >
            <option value="">Tất cả khoa phòng</option>
            {DEPARTMENTS.map((item) => (
              <option key={item}>{item}</option>
            ))}
          </select>
          <select
            aria-label="Lọc học hàm học vị"
            className="rounded border border-border bg-white px-3 py-2.5 text-xs"
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
            className="rounded border border-border bg-white px-3 py-2.5 text-xs"
            value={status}
            onChange={(event) => updateFilter(setStatus, event.target.value)}
          >
            <option value="">Tất cả trạng thái</option>
            <option value="true">Đang nhận lịch</option>
            <option value="false">Tạm ngừng nhận lịch</option>
          </select>
          <Button variant="secondary" size="sm" onClick={() => downloadCsv(filtered)}>
            <img src="/assets/admin-doctors/list-17.svg" alt="" />
            Xuất CSV
          </Button>
          <Button variant="secondary" size="sm" disabled title="Tùy chọn cột sẽ có ở phiên bản sau">
            <img src="/assets/admin-doctors/list-18.svg" alt="" />
          </Button>
          <Link
            to="/admin/doctors/new"
            className="inline-flex items-center gap-2 rounded-lg bg-primary px-4 py-2.5 text-xs font-bold text-white shadow-sm hover:bg-primary-dark"
          >
            <img src="/assets/admin-doctors/list-19.svg" alt="" />
            Thêm Hồ sơ Bác sĩ <kbd className="rounded bg-white/20 px-1.5 py-0.5">F2</kbd>
          </Link>
        </section>
        <section
          className="overflow-hidden rounded-lg border border-border bg-white shadow-sm"
          aria-label="Danh sách bác sĩ"
        >
          <DoctorTable
            doctors={visible}
            selectedIds={selectedIds}
            onToggleSelection={toggleSelection}
            onToggleAll={toggleAll}
            onToggleStatus={(id) => {
              const doctor = doctors.find((item) => item.id === id);
              if (doctor) updateDoctor({ ...doctor, active: !doctor.active });
            }}
          />
          <DoctorPagination
            count={filtered.length}
            currentPage={currentPage}
            pageSize={PAGE_SIZE}
            totalPages={totalPages}
            onPageChange={setPage}
          />
        </section>
      </main>
      <Outlet />
    </AdminDoctorShell>
  );
}
