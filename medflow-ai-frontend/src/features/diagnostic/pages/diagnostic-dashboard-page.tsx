import { useMemo, useState } from 'react';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { DiagnosticQueueTable } from '../components/diagnostic-queue-table';
import { DiagnosticQuickActions } from '../components/diagnostic-quick-actions';
import { DiagnosticSidebar } from '../components/diagnostic-sidebar';
import { DiagnosticStatCards } from '../components/diagnostic-stat-cards';
import {
  DIAGNOSTIC_QUEUE_MOCK,
  DIAGNOSTIC_QUICK_ACTIONS_MOCK,
  DIAGNOSTIC_STATS_MOCK,
  PENDING_RESULTS_MOCK,
} from '../data/diagnostic-mock-data';
import type { DiagnosticCategory, PendingResultEntry } from '../types/diagnostic.types';

const RESULT_STATUS_LABEL: Record<
  PendingResultEntry['status'],
  { label: string; tone: 'amber' | 'sky' | 'emerald' }
> = {
  DRAFT: { label: 'Bản nháp', tone: 'amber' },
  PENDING_VERIFY: { label: 'Chờ duyệt KQ', tone: 'sky' },
  FINALIZED: { label: 'Đã trả BS', tone: 'emerald' },
};

const QUICK_ACTION_TO_NAV: Record<string, string> = {
  'confirm-order': 'orders',
  'receive-patient': 'queue',
  'update-execution': 'execution',
  'enter-result': 'results',
};

export function DiagnosticDashboardPage() {
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();
  const [todayLabel] = useState(() => new Date().toLocaleDateString('vi-VN'));
  const [activeNav, setActiveNav] = useState('dashboard');
  const [selectedActionLabel, setSelectedActionLabel] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedCategory, setSelectedCategory] = useState<'ALL' | DiagnosticCategory>('ALL');

  const handleQuickAction = (actionId: string) => {
    setActiveNav(QUICK_ACTION_TO_NAV[actionId] ?? 'dashboard');
    const matched = DIAGNOSTIC_QUICK_ACTIONS_MOCK.find((item) => item.id === actionId);
    setSelectedActionLabel(matched?.label ?? null);
  };

  const filteredQueue = useMemo(() => {
    const keyword = searchQuery.trim().toLowerCase();
    return DIAGNOSTIC_QUEUE_MOCK.filter((entry) => {
      const matchesCategory = selectedCategory === 'ALL' || entry.category === selectedCategory;
      const haystack =
        `${entry.orderCode} ${entry.ticketNo} ${entry.patientName} ${entry.patientCode} ${entry.serviceName}`.toLowerCase();
      return matchesCategory && (!keyword || haystack.includes(keyword));
    });
  }, [searchQuery, selectedCategory]);

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DiagnosticSidebar activeItem={activeNav} onItemSelect={setActiveNav} />

      <div className="flex-1 flex flex-col min-w-0">
        <header className="h-16 bg-white border-b border-slate-200 px-6 flex items-center justify-between">
          <div>
            <div className="text-sm font-bold text-slate-900">Tổng quan cận lâm sàng</div>
            <div className="text-[11px] text-slate-500">Ca sáng • Hôm nay, {todayLabel}</div>
          </div>
          <div className="flex items-center gap-3">
            <button
              type="button"
              aria-label="Thông báo cận lâm sàng"
              className="p-2 rounded-lg text-slate-400 hover:text-slate-700 hover:bg-slate-100 transition-colors relative"
              title="Thông báo cận lâm sàng"
            >
              <svg
                aria-hidden="true"
                className="w-5 h-5"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth="2"
                  d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9"
                />
              </svg>
              <span className="absolute top-1.5 right-1.5 w-2 h-2 rounded-full bg-rose-500" />
            </button>
            <div className="flex items-center gap-2.5 pl-3 border-l border-slate-200">
              <div className="w-9 h-9 rounded-full bg-sky-100 border border-sky-200 text-sky-700 flex items-center justify-center font-bold text-xs">
                {(user?.fullName ?? 'CL').slice(0, 2).toUpperCase()}
              </div>
              <div className="text-right">
                <div className="text-xs font-bold text-slate-800">
                  {user?.fullName ?? 'KTV. Cận lâm sàng'}
                </div>
                <button
                  type="button"
                  onClick={() => logout.mutate()}
                  className="text-[10px] text-slate-400 hover:text-rose-600 transition-colors"
                >
                  Đăng xuất
                </button>
              </div>
            </div>
          </div>
        </header>

        <main className="flex-1 p-6 space-y-6 overflow-y-auto">
          <DiagnosticStatCards stats={DIAGNOSTIC_STATS_MOCK} />

          <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
            <Card className="xl:col-span-2">
              <CardHeader className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
                <div>
                  <div className="text-sm font-bold text-slate-900">
                    Danh sách & hàng đợi chỉ định CLS
                  </div>
                  <div className="text-[11px] text-slate-500">
                    {filteredQueue.length} chỉ định • đồng bộ trực tiếp với phòng khám
                  </div>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <input
                    type="search"
                    value={searchQuery}
                    onChange={(e) => setSearchQuery(e.target.value)}
                    placeholder="Tìm mã CĐ, tên BN, dịch vụ..."
                    aria-label="Tìm kiếm chỉ định CLS"
                    className="h-8 rounded-lg border border-slate-200 bg-slate-50 px-2.5 text-xs text-slate-800 placeholder:text-slate-400 focus:border-sky-500 focus:bg-white focus:outline-none"
                  />
                  <select
                    value={selectedCategory}
                    onChange={(e) =>
                      setSelectedCategory(e.target.value as 'ALL' | DiagnosticCategory)
                    }
                    aria-label="Lọc nhóm cận lâm sàng"
                    className="h-8 rounded-lg border border-slate-200 bg-slate-50 px-2 text-xs text-slate-700 focus:border-sky-500 focus:bg-white focus:outline-none"
                  >
                    <option value="ALL">Tất cả nhóm CLS</option>
                    <option value="LABORATORY">Xét nghiệm</option>
                    <option value="IMAGING">Chẩn đoán hình ảnh</option>
                    <option value="FUNCTIONAL">Thăm dò chức năng</option>
                  </select>
                </div>
              </CardHeader>
              <CardContent className="p-0 px-6 pb-4">
                <DiagnosticQueueTable entries={filteredQueue} />
              </CardContent>
            </Card>

            <div className="space-y-6">
              <Card>
                <CardHeader>
                  <div className="text-sm font-bold text-slate-900">Thao tác nghiệp vụ CLS</div>
                </CardHeader>
                <CardContent className="pt-0 space-y-2.5">
                  <DiagnosticQuickActions
                    actions={DIAGNOSTIC_QUICK_ACTIONS_MOCK}
                    onAction={handleQuickAction}
                  />
                  {selectedActionLabel && (
                    <div className="rounded-lg border border-sky-100 bg-sky-50 px-3 py-2 text-[11px] text-sky-700">
                      Đang chọn lối vào:{' '}
                      <span className="font-semibold">{selectedActionLabel}</span>
                    </div>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader className="flex items-center justify-between">
                  <div className="text-sm font-bold text-slate-900">Kết quả chờ duyệt & trả</div>
                  <Badge tone="sky">{PENDING_RESULTS_MOCK.length} phiếu</Badge>
                </CardHeader>
                <CardContent className="p-0">
                  <ul className="divide-y divide-slate-100">
                    {PENDING_RESULTS_MOCK.map((item) => (
                      <li key={item.id} className="px-6 py-3 flex items-center gap-3">
                        <div className="w-12 text-center font-mono text-xs font-bold text-slate-700">
                          {item.updatedAt}
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className="text-xs font-semibold text-slate-800 truncate">
                            {item.patientName} • {item.orderCode}
                          </div>
                          <div className="text-[10px] text-slate-500 truncate">
                            {item.serviceName} ({item.orderingDoctor})
                          </div>
                        </div>
                        <Badge tone={RESULT_STATUS_LABEL[item.status].tone}>
                          {RESULT_STATUS_LABEL[item.status].label}
                        </Badge>
                      </li>
                    ))}
                  </ul>
                </CardContent>
              </Card>
            </div>
          </div>
        </main>
      </div>
    </div>
  );
}
