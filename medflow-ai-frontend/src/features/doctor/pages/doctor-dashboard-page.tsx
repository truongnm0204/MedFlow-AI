import { RefreshCw } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { AssignedQueueTable } from '../components/assigned-queue-table';
import { DoctorQuickActions } from '../components/doctor-quick-actions';
import { DoctorSidebar } from '../components/doctor-sidebar';
import { DoctorStatCards } from '../components/doctor-stat-cards';
import {
  ASSIGNED_QUEUE_MOCK,
  DOCTOR_QUICK_ACTIONS_MOCK,
  DOCTOR_STATS_MOCK,
  DOCTOR_WORKBENCH_META,
  PENDING_DIAGNOSTICS_MOCK,
} from '../data/doctor-mock-data';
import { useDoctorDashboard } from '../hooks/use-doctor-dashboard';
import type { AssignedQueueStatus, PendingDiagnosticItem } from '../types/doctor.types';
type QueueFilter = 'ALL' | AssignedQueueStatus;

const QUEUE_FILTER_OPTIONS: { id: QueueFilter; label: string }[] = [
  { id: 'ALL', label: 'Tất cả' },
  { id: 'WAITING', label: 'Chờ gọi' },
  { id: 'IN_CONSULTATION', label: 'Đang khám' },
  { id: 'AWAITING_RESULTS', label: 'Chờ CLS' },
];

const DIAGNOSTIC_STATUS_META: Record<
  PendingDiagnosticItem['status'],
  { label: string; tone: 'emerald' | 'amber' }
> = {
  RESULT_READY: { label: 'Đã có kết quả', tone: 'emerald' },
  IN_PROGRESS: { label: 'Đang thực hiện', tone: 'amber' },
};

export function DoctorDashboardPage() {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();
  const { data, isPending, isError, error, refetch } = useDoctorDashboard(undefined, true);

  const [todayLabel] = useState(() => new Date().toLocaleDateString('vi-VN'));
  const [activeNav, setActiveNav] = useState('dashboard');
  const [queueFilter, setQueueFilter] = useState<QueueFilter>('ALL');
  const [searchTerm, setSearchTerm] = useState('');

  const doctorDisplayName =
    user?.fullName?.trim() ||
    data?.doctorProfile?.displayName?.trim() ||
    data?.doctorProfile?.fullName?.trim() ||
    DOCTOR_WORKBENCH_META.defaultDoctorName;

  const staffCode =
    user?.staffCode?.trim() ||
    data?.doctorProfile?.staffCode?.trim() ||
    data?.doctorProfile?.doctorCode?.trim() ||
    null;

  const roomCode = data?.roomCode || DOCTOR_WORKBENCH_META.roomCode;
  const departmentName = data?.departmentName || DOCTOR_WORKBENCH_META.departmentName;
  const shiftLabel = data?.shiftLabel || DOCTOR_WORKBENCH_META.shiftLabel;
  const stats = data?.stats ?? DOCTOR_STATS_MOCK;
  const assignedQueue = data?.assignedQueue ?? ASSIGNED_QUEUE_MOCK;
  const pendingDiagnostics = data?.pendingDiagnostics ?? PENDING_DIAGNOSTICS_MOCK;
  const quickActions = data?.quickActions ?? DOCTOR_QUICK_ACTIONS_MOCK;

  const filteredQueue = useMemo(() => {
    const keyword = searchTerm.trim().toLowerCase();
    return assignedQueue.filter((item) => {
      const matchesStatus = queueFilter === 'ALL' || item.status === queueFilter;
      const matchesKeyword =
        keyword.length === 0 ||
        item.patientName.toLowerCase().includes(keyword) ||
        item.patientCode.toLowerCase().includes(keyword) ||
        item.queueNo.toLowerCase().includes(keyword);
      return matchesStatus && matchesKeyword;
    });
  }, [assignedQueue, queueFilter, searchTerm]);

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DoctorSidebar
        activeItem={activeNav}
        doctorName={doctorDisplayName}
        roomLabel={`${roomCode} • ${departmentName}`}
        onItemSelect={(item) => {
          if (item === 'assigned-queue') {
            navigate('/doctor/assigned-queue');
            return;
          }
          setActiveNav(item);
        }}
      />

      <div className="flex-1 flex flex-col min-w-0">
        <header className="h-16 bg-white border-b border-slate-200 px-6 flex items-center justify-between">
          <div>
            <h1 className="text-sm font-bold text-slate-900">Tổng quan bàn khám bác sĩ</h1>
            <div className="text-[11px] text-slate-500">
              {roomCode} • {departmentName} • {shiftLabel} • Hôm nay, {todayLabel}
            </div>
          </div>
          <div className="flex items-center gap-3">
            {staffCode && (
              <span className="hidden sm:inline-flex rounded-md border border-slate-200 bg-slate-50 px-2 py-0.5 font-mono text-[11px] font-semibold text-slate-700">
                Mã NV: {staffCode}
              </span>
            )}
            <button
              type="button"
              onClick={() => void refetch()}
              disabled={isPending}
              aria-label="Làm mới dữ liệu bàn khám"
              className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-2.5 py-1.5 text-xs font-semibold text-slate-700 transition-colors hover:bg-slate-50 disabled:opacity-60"
            >
              <RefreshCw
                aria-hidden="true"
                className={`h-3.5 w-3.5 ${isPending ? 'animate-spin' : ''}`}
              />
              <span className="hidden sm:inline">Làm mới</span>
            </button>
            <div className="flex items-center gap-2.5 pl-3 border-l border-slate-200">
              <div className="w-9 h-9 rounded-full bg-sky-100 border border-sky-200 text-sky-700 flex items-center justify-center font-bold text-xs">
                {doctorDisplayName.slice(0, 2).toUpperCase()}
              </div>
              <div className="text-right">
                <div className="text-xs font-bold text-slate-800">{doctorDisplayName}</div>
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
          {isError && (
            <Alert tone="error">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span>{getErrorMessage(error, 'Không thể tải dữ liệu bàn khám bác sĩ')}</span>
                <button
                  type="button"
                  onClick={() => void refetch()}
                  className="rounded-lg bg-rose-600 px-3 py-1 text-xs font-semibold text-white transition-colors hover:bg-rose-700"
                >
                  Thử lại
                </button>
              </div>
            </Alert>
          )}
          <DoctorStatCards stats={stats} />
          <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
            <Card className="xl:col-span-2">
              <CardHeader className="space-y-3">
                <div className="flex items-center justify-between gap-2">
                  <div>
                    <div className="text-sm font-bold text-slate-900">
                      Hàng đợi bệnh nhân được phân công
                    </div>
                    <div className="text-[11px] text-slate-500">
                      {filteredQueue.length}/{assignedQueue.length} ca tại phòng {roomCode}
                    </div>
                  </div>
                  <Badge tone="sky">Trực tiếp</Badge>
                </div>

                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="flex flex-wrap gap-1.5">
                    {QUEUE_FILTER_OPTIONS.map((opt) => (
                      <button
                        key={opt.id}
                        type="button"
                        onClick={() => setQueueFilter(opt.id)}
                        className={`px-2.5 py-1 rounded-lg text-xs font-semibold transition-colors ${
                          queueFilter === opt.id
                            ? 'bg-sky-600 text-white'
                            : 'bg-slate-100 text-slate-600 hover:bg-slate-200'
                        }`}
                      >
                        {opt.label}
                      </button>
                    ))}
                  </div>
                  <input
                    type="search"
                    value={searchTerm}
                    onChange={(e) => setSearchTerm(e.target.value)}
                    placeholder="Tìm tên, mã BN, số TT..."
                    aria-label="Tìm bệnh nhân trong hàng đợi"
                    className="w-full sm:w-56 px-3 py-1.5 rounded-lg border border-slate-200 text-xs focus:outline-none focus:border-sky-500"
                  />
                </div>
              </CardHeader>
              <CardContent className="p-0 px-6 pb-4">
                <AssignedQueueTable entries={filteredQueue} />
              </CardContent>
            </Card>
            <div className="space-y-6">
              <Card>
                <CardHeader>
                  <div className="text-sm font-bold text-slate-900">Lối vào nghiệp vụ bác sĩ</div>
                </CardHeader>
                <CardContent className="pt-0">
                  <DoctorQuickActions
                    actions={quickActions}
                    // TODO(M2/M3): nối thao tác nhanh tới luồng gọi số, khám lâm sàng, chỉ định CLS và kê đơn.
                    onAction={(_id) => undefined}
                  />
                </CardContent>
              </Card>

              <Card>
                <CardHeader className="flex items-center justify-between">
                  <div className="text-sm font-bold text-slate-900">Kết quả cận lâm sàng</div>
                  <Badge tone="amber">{pendingDiagnostics.length} phiếu</Badge>
                </CardHeader>
                <CardContent className="p-0">
                  <ul className="divide-y divide-slate-100">
                    {pendingDiagnostics.map((diag) => (
                      <li key={diag.id} className="px-6 py-3 flex items-center gap-3">
                        <div className="w-11 text-center font-mono text-xs font-bold text-slate-700">
                          {diag.orderedAt}
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className="text-xs font-semibold text-slate-800 truncate">
                            {diag.patientName} • {diag.orderCode}
                          </div>
                          <div className="text-[11px] text-slate-500 truncate">
                            {diag.serviceName}
                          </div>
                        </div>
                        <Badge tone={DIAGNOSTIC_STATUS_META[diag.status].tone}>
                          {DIAGNOSTIC_STATUS_META[diag.status].label}
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
