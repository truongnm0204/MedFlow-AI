import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Search } from 'lucide-react';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { DiagnosticSidebar } from '../components/diagnostic-sidebar';
import { DiagnosticTrackingStatCards } from '../components/diagnostic-tracking-stat-cards';
import { DiagnosticTrackingTable } from '../components/diagnostic-tracking-table';
import { DiagnosticVisitSummaryModal } from '../components/diagnostic-visit-summary-modal';
import {
  TRACKING_STATS_MOCK,
  TRACKING_VISITS_MOCK,
} from '../data/diagnostic-results-tracking-mock';
import type {
  TrackingStatusFilter,
  TrackingVisit,
} from '../types/diagnostic-results-tracking.types';

const FILTERS: { value: TrackingStatusFilter; label: string }[] = [
  { value: 'ALL', label: 'Tất cả' },
  { value: 'COMPLETED', label: 'Hoàn tất 100%' },
  { value: 'IN_PROGRESS', label: 'Đang thực hiện' },
  { value: 'PENDING_SIGN', label: 'Chờ ký duyệt' },
  { value: 'ABNORMAL', label: 'Có cảnh báo bất thường' },
];

export function DiagnosticResultsTrackingPage() {
  const user = useAuthStore((s) => s.user);
  const navigate = useNavigate();
  const logout = useLogout();
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<TrackingStatusFilter>('ALL');
  const [selectedVisit, setSelectedVisit] = useState<TrackingVisit | null>(null);
  const [activeNav, setActiveNav] = useState('results');

  const displayName = user?.fullName?.trim() || 'KTV. Cận lâm sàng';

  const filteredVisits = useMemo(() => {
    const keyword = searchQuery.trim().toLowerCase();
    return TRACKING_VISITS_MOCK.filter((visit) => {
      const matchesStatus =
        statusFilter === 'ALL'
          ? true
          : statusFilter === 'ABNORMAL'
            ? visit.orders.some((o) => o.isAbnormal)
            : visit.status === statusFilter;
      const haystack =
        `${visit.visitCode} ${visit.patientCode} ${visit.patientName} ${visit.orderingDoctor}`.toLowerCase();
      return matchesStatus && (!keyword || haystack.includes(keyword));
    });
  }, [searchQuery, statusFilter]);

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DiagnosticSidebar
        activeItem={activeNav}
        onItemSelect={(item) => {
          setActiveNav(item);
          if (item === 'dashboard') navigate('/diagnostic');
          if (item === 'orders') navigate('/diagnostic/orders');
        }}
        staffName={displayName}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 items-center justify-between border-b border-border bg-white px-6">
          <div>
            <div className="text-sm font-bold text-navy">Theo dõi kết quả cận lâm sàng</div>
            <div className="text-[11px] text-sub">Tổng hợp chỉ định & kết quả theo lượt khám</div>
          </div>
          <div className="flex items-center gap-3 text-right">
            <div className="text-xs font-bold text-navy">{displayName}</div>
            <button
              type="button"
              onClick={() => logout.mutate()}
              className="text-[10px] text-sub transition-colors hover:text-rose-600"
            >
              Đăng xuất
            </button>
          </div>
        </header>

        <main className="flex-1 space-y-6 overflow-y-auto p-6">
          <DiagnosticTrackingStatCards stats={TRACKING_STATS_MOCK} />

          <Card>
            <CardHeader className="flex flex-col gap-3">
              <div className="relative max-w-md">
                <Search
                  className="absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-sub"
                  aria-hidden="true"
                />
                <input
                  type="search"
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  placeholder="Tìm mã lượt khám, mã BN, tên BN, BS chỉ định..."
                  aria-label="Tìm kiếm lượt khám CLS"
                  className="h-9 w-full rounded-lg border border-border bg-canvas pl-8 pr-2.5 text-xs text-ink placeholder:text-sub focus:border-primary focus:bg-white focus:outline-none"
                />
              </div>
              <div className="flex flex-wrap gap-2" role="group" aria-label="Lọc trạng thái">
                {FILTERS.map((f) => (
                  <button
                    key={f.value}
                    type="button"
                    aria-label={`Lọc ${f.label}`}
                    aria-pressed={statusFilter === f.value}
                    onClick={() => setStatusFilter(f.value)}
                    className={`rounded-full border px-3 py-1 text-[11px] font-semibold transition-colors ${
                      statusFilter === f.value
                        ? 'border-primary bg-primary text-white'
                        : 'border-border bg-white text-navy hover:bg-canvas'
                    }`}
                  >
                    {f.label}
                  </button>
                ))}
              </div>
            </CardHeader>
            <CardContent className="pt-0">
              <DiagnosticTrackingTable visits={filteredVisits} onViewSummary={setSelectedVisit} />
            </CardContent>
          </Card>
        </main>
      </div>

      <DiagnosticVisitSummaryModal visit={selectedVisit} onClose={() => setSelectedVisit(null)} />
    </div>
  );
}
