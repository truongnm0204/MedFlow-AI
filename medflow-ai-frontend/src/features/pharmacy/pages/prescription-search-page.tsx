import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft, Pill, RefreshCw } from 'lucide-react';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';

import { PrescriptionSearchFiltersBar } from '../components/prescription-search-filters';
import { PrescriptionSearchMetrics } from '../components/prescription-search-metrics';
import { PrescriptionSearchTable } from '../components/prescription-search-table';
import { PharmacySidebar } from '../components/pharmacy-sidebar';
import { PHARMACY_WORKBENCH_META } from '../data/pharmacy-mock-data';
import { usePharmacyPrescriptionSearch } from '../hooks/use-pharmacy-workbench';
import type {
  PrescriptionSearchFilters,
  PrescriptionSearchItem,
  PrescriptionSearchMetrics as PrescriptionSearchMetricsType,
} from '../types/prescription-search.types';

const EMPTY_METRICS: PrescriptionSearchMetricsType = {
  pendingApproval: 0,
  preparing: 0,
  readyForDispense: 0,
  completedInShift: 0,
};

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

  const searchQuery = usePharmacyPrescriptionSearch();
  const sourceItems = useMemo(() => searchQuery.data?.items ?? [], [searchQuery.data]);
  const metrics = searchQuery.data?.metrics ?? EMPTY_METRICS;

  const doctors = useMemo(() => {
    return Array.from(
      new Set(sourceItems.map((p) => p.doctorName).filter((name) => Boolean(name))),
    );
  }, [sourceItems]);

  const counts = useMemo(() => {
    return {
      all: sourceItems.length,
      pendingApproval: sourceItems.filter((p) => p.status === 'PENDING_APPROVAL').length,
      preparing: sourceItems.filter((p) => p.status === 'PREPARING').length,
      readyForDispense: sourceItems.filter((p) => p.status === 'READY_FOR_DISPENSE').length,
    };
  }, [sourceItems]);

  const filteredItems = useMemo(() => {
    const q = filters.query.trim().toLowerCase();
    return sourceItems.filter((item) => {
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
  }, [filters, sourceItems]);

  const handleFilterChange = (patch: Partial<PrescriptionSearchFilters>) => {
    setFilters((prev) => ({ ...prev, ...patch }));
  };

  const handlePrepare = (item: PrescriptionSearchItem) => {
    setNotice(`Đã tiếp nhận đơn thuốc ${item.rxCode} của bệnh nhân ${item.patientName}.`);
    if (item.status === 'READY_FOR_DISPENSE') {
      navigate(`/pharmacy/prescriptions/${encodeURIComponent(item.rxCode)}/dispense`);
      return;
    }
    navigate(`/pharmacy/prescriptions/${encodeURIComponent(item.rxCode)}/prepare`);
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
              <h1 className="flex items-center gap-2 truncate text-sm font-bold text-navy">
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
          <PrescriptionSearchMetrics metrics={metrics} />

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

          {searchQuery.isPending && (
            <div
              role="status"
              className="rounded-2xl border border-border bg-white p-8 text-center text-xs text-sub"
            >
              Đang tải danh sách đơn thuốc từ máy chủ...
            </div>
          )}

          {!searchQuery.isPending && searchQuery.isError && (
            <div role="alert">
              <Alert tone="error">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <span>
                    {searchQuery.error instanceof Error
                      ? searchQuery.error.message
                      : 'Không thể tải danh sách đơn thuốc.'}
                  </span>
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    onClick={() => void searchQuery.refetch()}
                    className="gap-1.5"
                  >
                    <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                    <span>Thử lại</span>
                  </Button>
                </div>
              </Alert>
            </div>
          )}

          {!searchQuery.isPending && !searchQuery.isError && (
            <PrescriptionSearchTable items={filteredItems} onPreparePrescription={handlePrepare} />
          )}
        </main>
      </div>
    </div>
  );
}
