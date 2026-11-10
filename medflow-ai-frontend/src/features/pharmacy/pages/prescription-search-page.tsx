import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft, Pill } from 'lucide-react';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';

import { PrescriptionSearchFiltersBar } from '../components/prescription-search-filters';
import { PrescriptionSearchMetrics } from '../components/prescription-search-metrics';
import { PrescriptionSearchTable } from '../components/prescription-search-table';
import { PharmacySidebar } from '../components/pharmacy-sidebar';
import { PHARMACY_WORKBENCH_META } from '../data/pharmacy-mock-data';
import {
  PRESCRIPTION_SEARCH_LIST_MOCK,
  PRESCRIPTION_SEARCH_METRICS_MOCK,
} from '../data/prescription-search-mock';
import type {
  PrescriptionSearchFilters,
  PrescriptionSearchItem,
} from '../types/prescription-search.types';

export function PrescriptionSearchPage() {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const [filters, setFilters] = useState<PrescriptionSearchFilters>({
    query: '',
    status: 'ALL',
    doctor: '',
  });
  const [notice, setNotice] = useState<string | null>(null);

  const doctors = useMemo(() => {
    return Array.from(new Set(PRESCRIPTION_SEARCH_LIST_MOCK.map((p) => p.doctorName)));
  }, []);

  const counts = useMemo(() => {
    return {
      all: PRESCRIPTION_SEARCH_LIST_MOCK.length,
      pendingApproval: PRESCRIPTION_SEARCH_LIST_MOCK.filter((p) => p.status === 'PENDING_APPROVAL')
        .length,
      preparing: PRESCRIPTION_SEARCH_LIST_MOCK.filter((p) => p.status === 'PREPARING').length,
      readyForDispense: PRESCRIPTION_SEARCH_LIST_MOCK.filter(
        (p) => p.status === 'READY_FOR_DISPENSE',
      ).length,
    };
  }, []);

  const filteredItems = useMemo(() => {
    const q = filters.query.trim().toLowerCase();
    return PRESCRIPTION_SEARCH_LIST_MOCK.filter((item) => {
      const matchesQuery =
        !q ||
        item.rxCode.toLowerCase().includes(q) ||
        item.patientCode.toLowerCase().includes(q) ||
        item.patientName.toLowerCase().includes(q) ||
        item.diagnosisText.toLowerCase().includes(q);
      if (!matchesQuery) return false;

      if (filters.status !== 'ALL' && item.status !== filters.status) return false;
      if (filters.doctor && item.doctorName !== filters.doctor) return false;

      return true;
    });
  }, [filters]);

  const handleFilterChange = (patch: Partial<PrescriptionSearchFilters>) => {
    setFilters((prev) => ({ ...prev, ...patch }));
  };

  const handlePrepare = (item: PrescriptionSearchItem) => {
    setNotice(`Đã tiếp nhận đơn thuốc ${item.rxCode} của bệnh nhân ${item.patientName}.`);
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <PharmacySidebar
        activeItem="search"
        pharmacistName={user?.fullName ?? PHARMACY_WORKBENCH_META.defaultPharmacistName}
        counterLabel={PHARMACY_WORKBENCH_META.counterLabel}
        onItemSelect={(id) => {
          if (id === 'dashboard') navigate('/pharmacy');
        }}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 items-center justify-between gap-4 border-b border-border bg-white px-6">
          <div className="flex min-w-0 items-center gap-3">
            <button
              type="button"
              onClick={() => navigate('/pharmacy')}
              aria-label="Quay lại tổng quan nhà thuốc"
              className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border bg-white text-navy transition-colors hover:bg-canvas"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            </button>
            <div className="min-w-0">
              <h1 className="truncate text-sm font-bold text-navy flex items-center gap-2">
                <Pill className="h-4 w-4 text-primary" aria-hidden="true" />
                <span>Hàng đợi Tiếp nhận &amp; Tra cứu Đơn thuốc (SCR-PHA-01)</span>
              </h1>
              <p className="truncate text-[11px] text-sub">
                Nhà thuốc Bệnh viện • Chỉ số vận hành real-time &amp; danh sách đơn ngoại trú
              </p>
            </div>
          </div>

          <button
            type="button"
            onClick={() => logout.mutate()}
            disabled={logout.isPending}
            className="rounded-lg border border-border px-3 py-1.5 text-xs font-medium text-ink transition-colors hover:bg-canvas disabled:opacity-50"
          >
            Đăng xuất
          </button>
        </header>

        <main className="flex-1 space-y-6 overflow-y-auto p-6">
          {/* 4 Stat cards */}
          <PrescriptionSearchMetrics metrics={PRESCRIPTION_SEARCH_METRICS_MOCK} />

          {/* Filters */}
          <PrescriptionSearchFiltersBar
            filters={filters}
            onChange={handleFilterChange}
            doctors={doctors}
            counts={counts}
            onScanBarcode={() =>
              setNotice('Đang kích hoạt đầu đọc mã vạch [F2] để quét mã đơn thuốc RX...')
            }
          />

          {notice && (
            <div role="status" aria-live="polite">
              <Alert tone="info">{notice}</Alert>
            </div>
          )}

          {/* Table */}
          <PrescriptionSearchTable items={filteredItems} onPreparePrescription={handlePrepare} />
        </main>
      </div>
    </div>
  );
}
