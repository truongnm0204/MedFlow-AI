import { useEffect, useMemo, useRef, useState } from 'react';
import { Outlet, useNavigate } from 'react-router-dom';
import { CheckCircle2, X } from 'lucide-react';

import { AdminSlotShell } from '../components/admin-slot-shell';
import { SlotBatchCloseModal } from '../components/slot-batch-close-modal';
import { SlotCloseModal } from '../components/slot-close-modal';
import { SlotFilters } from '../components/slot-filters';
import { SlotMetrics } from '../components/slot-metrics';
import { SlotPagination } from '../components/slot-pagination';
import { SlotReopenModal } from '../components/slot-reopen-modal';
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

  // Modal states for closing, reopening, and batch closing
  const [closingSlot, setClosingSlot] = useState<ExaminationSlot | null>(null);
  const [reopeningSlot, setReopeningSlot] = useState<ExaminationSlot | null>(null);
  const [batchClosingSlots, setBatchClosingSlots] = useState<ExaminationSlot[] | null>(null);
  const [feedbackMessage, setFeedbackMessage] = useState<string | null>(null);

  useEffect(() => {
    if (feedbackMessage) {
      const timer = setTimeout(() => setFeedbackMessage(null), 5000);
      return () => clearTimeout(timer);
    }
  }, [feedbackMessage]);
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
        {/* Operation Feedback Banner */}
        {feedbackMessage && (
          <div className="flex items-center justify-between gap-3 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-xs text-emerald-800 shadow-2xs animate-in fade-in duration-200">
            <div className="flex items-center gap-2">
              <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600" aria-hidden="true" />
              <span className="font-medium">{feedbackMessage}</span>
            </div>
            <button
              type="button"
              onClick={() => setFeedbackMessage(null)}
              className="rounded p-1 text-emerald-700 hover:bg-emerald-100 transition-colors"
              aria-label="Đóng thông báo"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          </div>
        )}

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
            onRequestCloseSlot={(slot) => setClosingSlot(slot)}
            onRequestReopenSlot={(slot) => setReopeningSlot(slot)}
            onRequestBatchClose={(selected) => setBatchClosingSlots(selected)}
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
      {/* Close Single Slot Modal */}
      <SlotCloseModal
        slot={closingSlot}
        open={Boolean(closingSlot)}
        onClose={() => setClosingSlot(null)}
        onSuccess={(slot, message) => {
          setFeedbackMessage(message || `Đã đóng khung giờ ${slot.code} thành công.`);
        }}
      />

      {/* Reopen Single Slot Modal */}
      <SlotReopenModal
        slot={reopeningSlot}
        open={Boolean(reopeningSlot)}
        onClose={() => setReopeningSlot(null)}
        onSuccess={(slot, message) => {
          setFeedbackMessage(message || `Đã mở lại khung giờ ${slot.code} thành công.`);
        }}
      />

      {/* Batch Close Modal */}
      <SlotBatchCloseModal
        slots={batchClosingSlots ?? []}
        open={Boolean(batchClosingSlots && batchClosingSlots.length > 0)}
        onClose={() => setBatchClosingSlots(null)}
        onSuccess={(count, message) => {
          setFeedbackMessage(message || `Đã đóng hàng loạt ${count} khung giờ thành công.`);
        }}
      />
    </AdminSlotShell>
  );
}
