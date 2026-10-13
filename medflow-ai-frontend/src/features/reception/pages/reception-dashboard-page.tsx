import { useState } from 'react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { useLogout } from '@/features/auth/hooks/use-auth';
import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { QuickActionsPanel } from '../components/quick-actions-panel';
import { QueuePreviewTable } from '../components/queue-preview-table';
import { ReceptionSidebar } from '../components/reception-sidebar';
import { ReceptionStatCards } from '../components/reception-stat-cards';
import {
  QUEUE_ENTRIES_MOCK,
  QUICK_ACTIONS_MOCK,
  RECEPTION_STATS_MOCK,
  TODAY_APPOINTMENTS_MOCK,
} from '../data/reception-mock-data';
import type { TodayAppointment } from '../types/reception.types';

const APPOINTMENT_STATUS_LABEL: Record<
  TodayAppointment['status'],
  { label: string; tone: 'sky' | 'emerald' | 'rose' | 'slate' }
> = {
  BOOKED: { label: 'Đã đặt', tone: 'sky' },
  CHECKED_IN: { label: 'Đã check-in', tone: 'emerald' },
  CANCELLED: { label: 'Đã hủy', tone: 'rose' },
  NO_SHOW: { label: 'Không đến', tone: 'slate' },
};

export function ReceptionDashboardPage() {
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();
  const [todayLabel] = useState(() => new Date().toLocaleDateString('vi-VN'));

  return (
    <div className="min-h-screen flex bg-[#F8FAFC] text-slate-800 antialiased">
      <ReceptionSidebar activeItem="dashboard" />

      <div className="flex-1 flex flex-col min-w-0">
        {/* Top bar */}
        <header className="h-16 bg-white border-b border-slate-200 px-6 flex items-center justify-between">
          <div>
            <div className="text-sm font-bold text-slate-900">Tổng quan quầy tiếp đón</div>
            <div className="text-[11px] text-slate-500">Ca sáng • Hôm nay, {todayLabel}</div>
          </div>
          <div className="flex items-center gap-3">
            <button
              type="button"
              className="p-2 rounded-lg text-slate-400 hover:text-slate-700 hover:bg-slate-100 transition-colors relative"
              title="Thông báo"
            >
              <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
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
              <div className="w-9 h-9 rounded-full bg-emerald-100 border border-emerald-200 text-emerald-700 flex items-center justify-center font-bold text-xs">
                {(user?.fullName ?? user?.username ?? 'LT').slice(0, 2).toUpperCase()}
              </div>
              <div className="text-right">
                <div className="text-xs font-bold text-slate-800">
                  {user?.fullName ?? user?.username ?? 'Nhân viên tiếp đón'}
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

        {/* Main */}
        <main className="flex-1 p-6 space-y-6 overflow-y-auto">
          <ReceptionStatCards stats={RECEPTION_STATS_MOCK} />

          <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
            {/* Queue preview */}
            <Card className="xl:col-span-2">
              <CardHeader className="flex items-center justify-between">
                <div>
                  <div className="text-sm font-bold text-slate-900">Hàng đợi hiện tại</div>
                  <div className="text-[11px] text-slate-500">
                    {QUEUE_ENTRIES_MOCK.length} lượt • cập nhật theo thời gian thực
                  </div>
                </div>
                <Badge tone="sky">Trực tiếp</Badge>
              </CardHeader>
              <CardContent className="p-0 px-6 pb-4">
                <QueuePreviewTable entries={QUEUE_ENTRIES_MOCK} />
              </CardContent>
            </Card>

            {/* Right column */}
            <div className="space-y-6">
              <Card>
                <CardHeader>
                  <div className="text-sm font-bold text-slate-900">Thao tác nhanh</div>
                </CardHeader>
                <CardContent className="pt-0">
                  <QuickActionsPanel
                    actions={QUICK_ACTIONS_MOCK}
                    // TODO(M2): wire quick actions toi flow that (check-in, walk-in, new patient, search).
                    onAction={(_id) => undefined}
                  />
                </CardContent>
              </Card>

              <Card>
                <CardHeader className="flex items-center justify-between">
                  <div className="text-sm font-bold text-slate-900">Lịch hẹn sắp tới</div>
                  <Badge tone="amber">{TODAY_APPOINTMENTS_MOCK.length} ca</Badge>
                </CardHeader>
                <CardContent className="p-0">
                  <ul className="divide-y divide-slate-100">
                    {TODAY_APPOINTMENTS_MOCK.map((ap) => (
                      <li key={ap.id} className="px-6 py-3 flex items-center gap-3">
                        <div className="w-12 text-center font-mono text-xs font-bold text-slate-700">
                          {ap.time}
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className="text-xs font-semibold text-slate-800 truncate">
                            {ap.patientName}
                          </div>
                          <div className="text-[10px] text-slate-500 truncate">
                            {ap.doctorName} • {ap.department}
                          </div>
                        </div>
                        <Badge tone={APPOINTMENT_STATUS_LABEL[ap.status].tone}>
                          {APPOINTMENT_STATUS_LABEL[ap.status].label}
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
