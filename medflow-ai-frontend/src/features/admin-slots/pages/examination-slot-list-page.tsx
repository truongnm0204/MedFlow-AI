import { useEffect, useMemo, useRef } from 'react';
import { Outlet, useNavigate } from 'react-router-dom';

import { AdminSlotShell } from '../components/admin-slot-shell';
import { SlotFilters } from '../components/slot-filters';
import { SlotMetrics } from '../components/slot-metrics';
import { SlotPagination } from '../components/slot-pagination';
import { SlotTable } from '../components/slot-table';
import { useExaminationSlotStore } from '../store/examination-slot.store';
import type { ExaminationSlot } from '../types/examination-slot.types';

function exportSlotsToCsv(slots: ExaminationSlot[]) {
  const quote = (val: string) => `"${val.replaceAll('"', '""').replace(/^[=+\-@]/, "'$&")}"`;
  const headers = [
    'Mã Slot',
    'Bác Sĩ',
    'Dịch Vụ Khám',
    'Mã Dịch Vụ',
    'Loại Dịch Vụ',
    'Chuyên Khoa',
    'Phòng',
    'Sức Chứa Đã Đặt',
    'Sức Chứa Tối Đa',
    'Ngày Khám',
    'Giờ Bắt Đầu',
    'Giờ Kết Thúc',
    'Trạng Thái',
  ];

  const rows = slots.map((s) => [
    s.code,
    s.doctorName,
    s.serviceName,
    s.serviceCode,
    s.serviceType === 'DIAGNOSTIC' ? 'Cận lâm sàng' : 'Ngoại trú',
    s.specialty,
    s.room,
    String(s.capacityBooked),
    String(s.capacityMax),
    s.date,
    s.startTime,
    s.endTime,
    s.status === 'OPEN' ? 'Đang mở' : 'Đã đóng',
  ]);

  const csv = '\uFEFF' + [headers, ...rows].map((row) => row.map(quote).join(',')).join('\r\n');
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = 'danh-sach-khung-gio-kham-medflow.csv';
  link.click();
  URL.revokeObjectURL(url);
}

export function ExaminationSlotListPage() {
  const navigate = useNavigate();
  const searchInputRef = useRef<HTMLInputElement>(null);

  const slots = useExaminationSlotStore((state) => state.slots);
  const searchQuery = useExaminationSlotStore((state) => state.searchQuery);
  const dateFilter = useExaminationSlotStore((state) => state.dateFilter);
  const specialtyFilter = useExaminationSlotStore((state) => state.specialtyFilter);
  const statusFilter = useExaminationSlotStore((state) => state.statusFilter);
  const page = useExaminationSlotStore((state) => state.page);
  const pageSize = useExaminationSlotStore((state) => state.pageSize);
  const setPage = useExaminationSlotStore((state) => state.setPage);
  const setPageSize = useExaminationSlotStore((state) => state.setPageSize);

  // Global hotkeys (Ctrl+K and F2)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.ctrlKey && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        searchInputRef.current?.focus();
      }
      if (e.key === 'F2') {
        e.preventDefault();
        navigate('/admin/slots/new');
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [navigate]);

  // Filtering pipeline
  const filteredSlots = useMemo(() => {
    return slots.filter((slot) => {
      const term = searchQuery.trim().toLowerCase();
      const matchesSearch =
        !term ||
        slot.code.toLowerCase().includes(term) ||
        slot.doctorName.toLowerCase().includes(term) ||
        slot.serviceName.toLowerCase().includes(term) ||
        slot.specialty.toLowerCase().includes(term) ||
        slot.room.toLowerCase().includes(term) ||
        slot.dateDisplay.toLowerCase().includes(term);

      const matchesDate = !dateFilter || slot.date === dateFilter;
      const matchesSpecialty = !specialtyFilter || slot.specialty === specialtyFilter;
      const matchesStatus = !statusFilter || slot.status === statusFilter;

      return matchesSearch && matchesDate && matchesSpecialty && matchesStatus;
    });
  }, [slots, searchQuery, dateFilter, specialtyFilter, statusFilter]);

  const totalPages = Math.max(1, Math.ceil(filteredSlots.length / pageSize));
  const currentPage = Math.min(page, totalPages);
  const paginatedSlots = filteredSlots.slice((currentPage - 1) * pageSize, currentPage * pageSize);

  return (
    <AdminSlotShell>
      <main className="space-y-5 px-6 py-6">
        {/* KPI Metric Cards */}
        <SlotMetrics slots={slots} />

        {/* Search & Filters Bar */}
        <SlotFilters
          searchRef={searchInputRef}
          onExportCsv={exportSlotsToCsv}
          filteredSlots={filteredSlots}
          onCreateSlot={() => navigate('/admin/slots/new')}
        />

        {/* Data Table with Batch Actions & Pagination */}
        <section
          className="overflow-hidden rounded-xl border border-border bg-white shadow-xs"
          aria-label="Danh sách khung giờ khám"
        >
          <SlotTable
            slots={paginatedSlots}
            onEditSlot={(slot) => navigate(`/admin/slots/${slot.id}/edit`)}
            onViewSchedule={(slot) => navigate(`/doctors/${slot.doctorId}`)}
            onViewDetail={(slot) => navigate(`/admin/slots/${slot.id}`)}
          />

          <SlotPagination
            count={filteredSlots.length}
            currentPage={currentPage}
            pageSize={pageSize}
            totalPages={totalPages}
            onPageChange={setPage}
            onPageSizeChange={setPageSize}
          />
        </section>
      </main>
      <Outlet />
    </AdminSlotShell>
  );
}
