import { AlertTriangle, Bell, FileCheck2, Search } from 'lucide-react';
import { useMemo, useState } from 'react';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { PharmacyQueueTable } from '../components/pharmacy-queue-table';
import { PharmacyQuickActions } from '../components/pharmacy-quick-actions';
import { PharmacySidebar } from '../components/pharmacy-sidebar';
import { PharmacyStatCards } from '../components/pharmacy-stat-cards';
import {
  FEFO_BATCH_ALERTS_MOCK,
  PHARMACY_QUICK_ACTIONS_MOCK,
  PHARMACY_STATS_MOCK,
  PHARMACY_WORKBENCH_META,
  PRESCRIPTION_QUEUE_MOCK,
} from '../data/pharmacy-mock-data';
import type {
  FefoBatchAlertStatus,
  PrescriptionQueueEntry,
  PrescriptionQueueStatus,
} from '../types/pharmacy.types';

type QueueFilter = 'ALL' | PrescriptionQueueStatus;

const QUEUE_FILTER_OPTIONS: { id: QueueFilter; label: string }[] = [
  { id: 'ALL', label: 'Tất cả' },
  { id: 'PENDING', label: 'Chờ soạn' },
  { id: 'PREPARING', label: 'Đang soạn' },
  { id: 'READY', label: 'Chờ phát' },
  { id: 'DISPENSED', label: 'Đã phát' },
];

const FEFO_ALERT_META: Record<
  FefoBatchAlertStatus,
  { label: string; tone: 'amber' | 'sky' | 'rose' }
> = {
  FEFO_PRIORITY: { label: 'Ưu tiên xuất', tone: 'amber' },
  LOW_STOCK: { label: 'Sắp chạm ngưỡng', tone: 'sky' },
  EXPIRED_LOCKED: { label: 'Khóa quá hạn', tone: 'rose' },
};

const QUICK_ACTION_TO_NAV: Record<string, string> = {
  'search-prescription': 'search',
  'prepare-medicine': 'prepare',
  'select-fefo-batch': 'prepare',
  'confirm-dispensing': 'dispense',
};

export function PharmacyDashboardPage() {
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();
  const [todayLabel] = useState(() => new Date().toLocaleDateString('vi-VN'));
  const [activeNav, setActiveNav] = useState('dashboard');
  const [selectedActionLabel, setSelectedActionLabel] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<QueueFilter>('ALL');
  const [selectedPrescription, setSelectedPrescription] = useState<PrescriptionQueueEntry>(
    () => PRESCRIPTION_QUEUE_MOCK[1] ?? PRESCRIPTION_QUEUE_MOCK[0],
  );

  const pharmacistDisplayName = user?.fullName ?? PHARMACY_WORKBENCH_META.defaultPharmacistName;

  const avatarInitials = useMemo(() => {
    const clean = pharmacistDisplayName.replace(/^DS\.\s*/i, '').trim();
    const parts = clean.split(/\s+/).filter(Boolean);
    if (parts.length >= 2) {
      return `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase();
    }
    return clean.slice(0, 2).toUpperCase() || 'DS';
  }, [pharmacistDisplayName]);

  const handleQuickAction = (actionId: string) => {
    setActiveNav(QUICK_ACTION_TO_NAV[actionId] ?? 'dashboard');
    const matched = PHARMACY_QUICK_ACTIONS_MOCK.find((item) => item.id === actionId);
    setSelectedActionLabel(matched?.label ?? null);
  };

  const filteredQueue = useMemo(() => {
    const keyword = searchQuery.trim().toLowerCase();
    return PRESCRIPTION_QUEUE_MOCK.filter((entry) => {
      const matchesStatus = statusFilter === 'ALL' || entry.status === statusFilter;
      const haystack =
        `${entry.prescriptionCode} ${entry.ticketNo} ${entry.patientName} ${entry.patientCode} ${entry.prescribingDoctor}`.toLowerCase();
      return matchesStatus && (!keyword || haystack.includes(keyword));
    });
  }, [searchQuery, statusFilter]);

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <PharmacySidebar
        activeItem={activeNav}
        pharmacistName={pharmacistDisplayName}
        counterLabel={PHARMACY_WORKBENCH_META.counterLabel}
        onItemSelect={setActiveNav}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 items-center justify-between border-b border-border bg-white px-6">
          <div>
            <h1 className="text-sm font-bold text-ink">Tổng quan nhà thuốc ngoại trú</h1>
            <p className="text-[11px] text-sub">
              Ca sáng • Hôm nay, {todayLabel} • Quầy cấp phát số 01
            </p>
          </div>

          <div className="flex items-center gap-3">
            <button
              type="button"
              aria-label="Thông báo nhà thuốc"
              className="relative rounded-lg p-2 text-sub transition-colors hover:bg-slate-100 hover:text-ink focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
              title="Thông báo nhà thuốc"
            >
              <Bell aria-hidden="true" className="h-5 w-5" />
              <span
                aria-hidden="true"
                className="absolute top-1.5 right-1.5 h-2 w-2 rounded-full bg-rose-500"
              />
            </button>

            <div className="flex items-center gap-2.5 border-l border-border pl-3">
              <div className="flex h-9 w-9 items-center justify-center rounded-full border border-sky-200 bg-sky-100 text-xs font-bold text-primary">
                {avatarInitials}
              </div>
              <div className="text-right">
                <div className="text-xs font-bold text-ink">{pharmacistDisplayName}</div>
                <button
                  type="button"
                  onClick={() => logout.mutate()}
                  className="text-[10px] text-sub transition-colors hover:text-rose-600"
                >
                  Đăng xuất
                </button>
              </div>
            </div>
          </div>
        </header>

        <main className="flex-1 space-y-6 overflow-y-auto p-6">
          <PharmacyStatCards stats={PHARMACY_STATS_MOCK} />

          <div className="grid grid-cols-1 gap-6 xl:grid-cols-3">
            {/* Left column (2/3): Prescription Queue + Selected Prescription Preview */}
            <Card className="xl:col-span-2">
              <CardHeader className="space-y-3">
                <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                  <div>
                    <h2 className="text-sm font-bold text-ink">
                      Danh sách &amp; hàng đợi đơn thuốc ngoại trú
                    </h2>
                    <p className="text-[11px] text-sub">
                      {filteredQueue.length} đơn trong ca • đồng bộ trực tiếp từ bàn khám &amp; cổng
                      thanh toán QR
                    </p>
                  </div>
                </div>

                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="relative w-full sm:w-64">
                    <Search
                      aria-hidden="true"
                      className="pointer-events-none absolute top-1/2 left-2.5 h-3.5 w-3.5 -translate-y-1/2 text-sub"
                    />
                    <input
                      type="search"
                      value={searchQuery}
                      onChange={(e) => setSearchQuery(e.target.value)}
                      placeholder="Tìm mã RX, mã BN, tên bệnh nhân..."
                      aria-label="Tìm kiếm đơn thuốc"
                      className="h-8 w-full rounded-lg border border-border bg-canvas pr-2.5 pl-8 text-xs text-ink placeholder:text-slate-400 focus:border-primary focus:bg-white focus:outline-none"
                    />
                  </div>

                  <div
                    role="group"
                    aria-label="Lọc trạng thái đơn thuốc"
                    className="flex flex-wrap items-center gap-1"
                  >
                    {QUEUE_FILTER_OPTIONS.map((opt) => {
                      const active = statusFilter === opt.id;
                      return (
                        <button
                          key={opt.id}
                          type="button"
                          aria-pressed={active}
                          onClick={() => setStatusFilter(opt.id)}
                          className={`h-8 rounded-lg border px-2.5 text-xs font-semibold whitespace-nowrap transition-colors ${
                            active
                              ? 'border-primary bg-primary text-white'
                              : 'border-border bg-white text-sub hover:bg-slate-50 hover:text-ink'
                          }`}
                        >
                          {opt.label}
                        </button>
                      );
                    })}
                  </div>
                </div>
              </CardHeader>

              <CardContent className="space-y-4 px-6 pt-2 pb-5">
                <PharmacyQueueTable
                  entries={filteredQueue}
                  selectedPrescriptionId={selectedPrescription?.id}
                  onSelectPrescription={setSelectedPrescription}
                />

                {selectedPrescription && (
                  <div className="flex flex-col justify-between gap-3 rounded-xl border border-sky-200 bg-sky-50/60 p-3.5 sm:flex-row sm:items-center">
                    <div className="min-w-0 space-y-1">
                      <div className="flex items-center gap-1.5 font-mono text-[11px] font-bold uppercase text-primary">
                        <FileCheck2 aria-hidden="true" className="h-3.5 w-3.5 shrink-0" />
                        <span>
                          Đang chọn: {selectedPrescription.prescriptionCode} •{' '}
                          {selectedPrescription.patientName} ({selectedPrescription.patientCode})
                        </span>
                      </div>
                      <div className="text-xs font-medium text-ink">
                        {selectedPrescription.items
                          .map(
                            (item, idx) =>
                              `${idx + 1}. ${item.medicineName} (${item.quantityLabel} • Lô ${item.batchCode})`,
                          )
                          .join('   ')}
                      </div>
                      <div className="text-[11px] text-sub">
                        Kiểm tra: {selectedPrescription.allergyNote}
                      </div>
                    </div>

                    <button
                      type="button"
                      onClick={() => handleQuickAction('prepare-medicine')}
                      className="shrink-0 rounded-lg bg-primary px-3.5 py-2 text-xs font-bold text-white shadow-2xs transition-colors hover:bg-primary-dark focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
                    >
                      Mở phiếu soạn
                    </button>
                  </div>
                )}
              </CardContent>
            </Card>

            {/* Right column (1/3): Quick Actions + FEFO Batch Alerts */}
            <div className="space-y-6">
              <Card>
                <CardHeader>
                  <h2 className="text-sm font-bold text-ink">Thao tác nghiệp vụ Dược</h2>
                  <p className="text-[11px] text-sub">Lối vào nhanh theo quyền Dược sĩ</p>
                </CardHeader>
                <CardContent className="space-y-3 pt-4">
                  <PharmacyQuickActions
                    actions={PHARMACY_QUICK_ACTIONS_MOCK}
                    onAction={handleQuickAction}
                  />
                  {selectedActionLabel && (
                    <div className="rounded-lg border border-sky-200 bg-sky-50 px-3 py-2 text-[11px] text-sky-800">
                      Đang chọn lối vào:{' '}
                      <span className="font-semibold">{selectedActionLabel}</span>
                    </div>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader className="flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <AlertTriangle aria-hidden="true" className="h-4 w-4 text-amber-600" />
                    <h2 className="text-sm font-bold text-ink">Kiểm soát lô thuốc (FEFO)</h2>
                  </div>
                  <Badge tone="amber">{FEFO_BATCH_ALERTS_MOCK.length} lô</Badge>
                </CardHeader>
                <CardContent className="p-0">
                  <ul className="divide-y divide-slate-100">
                    {FEFO_BATCH_ALERTS_MOCK.map((batch) => {
                      const alertMeta = FEFO_ALERT_META[batch.status];
                      return (
                        <li
                          key={batch.id}
                          className="flex items-center justify-between gap-3 px-6 py-3"
                        >
                          <div className="min-w-0">
                            <div className="truncate text-xs font-bold text-ink">
                              {batch.medicineName}
                            </div>
                            <div className="font-mono text-[10px] text-sub">
                              {batch.batchCode} • HSD: {batch.expiryDate}
                            </div>
                            <div className="text-[10px] text-sub">
                              Tồn khả dụng: {batch.availableStock}
                            </div>
                          </div>
                          <Badge tone={alertMeta.tone} className="shrink-0 whitespace-nowrap">
                            {alertMeta.label}
                          </Badge>
                        </li>
                      );
                    })}
                  </ul>
                  <div className="border-t border-border bg-canvas px-6 py-2.5 text-[11px] text-sub">
                    Chặn xuất kho tự động với lô hết hạn/blocked theo quy tắc FEFO.
                  </div>
                </CardContent>
              </Card>
            </div>
          </div>
        </main>
      </div>
    </div>
  );
}
