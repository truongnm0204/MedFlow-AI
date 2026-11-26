import { CalendarClock } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { useLogout } from '@/features/auth/hooks/use-auth';
import { hasPermission } from '@/features/auth/utils/rbac';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { CheckInModal } from '../components/checkin-modal';
import { CheckInResultModal } from '../components/checkin-result-modal';
import { CreatePatientModal } from '../components/create-patient-modal';
import { AssignExaminationRoomModal } from '../components/assign-examination-room-modal';
import { PatientDetailModal } from '../components/patient-detail-modal';
import { PrintExaminationTicketModal } from '../components/print-examination-ticket-modal';
import {
  ReceptionCashCollectionModal,
  type ReceptionCashCollectionTarget,
} from '../components/reception-cash-collection-modal';
import { ReceptionVisitResumeModal } from '../components/reception-visit-resume-modal';
import { UpdatePatientModal } from '../components/update-patient-modal';
import { TransferVisitRoomModal } from '../components/transfer-visit-room-modal';
import { QuickActionsPanel } from '../components/quick-actions-panel';
import { fetchCheckInResult } from '../api/reception.api';
import type { CheckInSuccessResult, ScheduledCheckInAppointment } from '../types/checkin.types';
import {
  useCheckInMutation,
  useCreatePatientMutation,
  usePatientDetail,
  useReceptionDashboardSummary,
  useReceptionPatientSearch,
  useUpdatePatientMutation,
} from '../hooks/use-reception-api';
import { useExaminationQueue } from '../hooks/use-examination-queue';
import { QueuePreviewTable } from '../components/queue-preview-table';
import { ReceptionSidebar } from '../components/reception-sidebar';
import { ReceptionStatCards } from '../components/reception-stat-cards';
import { QUICK_ACTIONS_MOCK } from '../data/reception-mock-data';
import type { AssignRoomVisitTarget } from '../types/assign-examination-room.types';
import type { QueueEntry, ReceptionStats, QueueStatus } from '../types/reception.types';
import type { UpdatePatientFormData } from '../types/update-patient.types';
import type { TransferVisitRoomTarget } from '../types/visit-room-transfer.types';

const APPOINTMENT_STATUS_LABEL: Record<
  string,
  { label: string; tone: 'sky' | 'emerald' | 'rose' | 'slate' }
> = {
  BOOKED: { label: 'Đã đặt', tone: 'sky' },
  CHECKED_IN: { label: 'Đã check-in', tone: 'emerald' },
  CANCELLED: { label: 'Đã hủy', tone: 'rose' },
  NO_SHOW: { label: 'Không đến', tone: 'slate' },
  IN_CONSULTATION: { label: 'Đang khám', tone: 'emerald' },
};

const QUEUE_STATUS_MAP: Record<string, QueueStatus> = {
  WAITING: 'WAITING',
  READY: 'WAITING',
  CALLED: 'CALLED',
  SERVING: 'IN_PROGRESS',
  IN_PROGRESS: 'IN_PROGRESS',
  COMPLETED: 'DONE',
  DONE: 'DONE',
  SKIPPED: 'SKIPPED',
};

function toQueueEntry(entry: {
  id: string;
  visitCode: string;
  ticketNo: string;
  patientName: string;
  patientCode: string;
  visitType: string;
  roomCode: string;
  roomName: string;
  doctorName: string;
  departmentName: string;
  status: string;
  priority: string;
  checkedInAt: string;
}): QueueEntry {
  return {
    id: entry.id,
    visitCode: entry.visitCode,
    ticketNo: entry.ticketNo,
    patientName: entry.patientName,
    patientCode: entry.patientCode,
    visitType: entry.visitType === 'WALK_IN' ? 'WALK_IN' : 'SCHEDULED',
    room: entry.roomCode || entry.roomName,
    doctorName: entry.doctorName,
    departmentName: entry.departmentName,
    status: QUEUE_STATUS_MAP[entry.status?.toUpperCase() ?? ''] ?? 'WAITING',
    checkedInAt: entry.checkedInAt,
    priority:
      entry.priority === 'ELDERLY' || entry.priority === 'PREGNANT' ? entry.priority : 'NORMAL',
  };
}

export function ReceptionDashboardPage() {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();
  const [createOpen, setCreateOpen] = useState(false);
  const [selectedPatientCode, setSelectedPatientCode] = useState<string | null>(null);
  const patientDetailQuery = usePatientDetail(selectedPatientCode);
  const selectedPatient = patientDetailQuery.data ?? null;
  const canAssignVisit = hasPermission(user?.roles, 'VISIT_ASSIGN');
  const canCheckInOrQueue =
    hasPermission(user?.roles, 'PATIENT_CHECKIN') || hasPermission(user?.roles, 'QUEUE_MANAGE');
  const canCollectCash = hasPermission(user?.roles, 'PAYMENT_COLLECT');
  const [cashTarget, setCashTarget] = useState<ReceptionCashCollectionTarget | null>(null);
  const [isCrossDayResumeOpen, setIsCrossDayResumeOpen] = useState(false);
  const [todayLabel] = useState(() => new Date().toLocaleDateString('vi-VN'));
  const summaryQuery = useReceptionDashboardSummary();
  const searchQuery = useReceptionPatientSearch('', 'ALL');
  const checkInMutation = useCheckInMutation();
  const createPatientMutation = useCreatePatientMutation();
  const updatePatientMutation = useUpdatePatientMutation();
  const queueQuery = useExaminationQueue();
  const [selectedCheckIn, setSelectedCheckIn] = useState<ScheduledCheckInAppointment | null>(null);
  const [resultError, setResultError] = useState<string | null>(null);
  const [selectedCheckInResult, setSelectedCheckInResult] = useState<CheckInSuccessResult | null>(
    null,
  );
  const [editingPatient, setEditingPatient] = useState<UpdatePatientFormData | null>(null);
  const [updateNotice, setUpdateNotice] = useState<string | null>(null);
  const [transferringTarget, setTransferringTarget] = useState<TransferVisitRoomTarget | null>(
    null,
  );
  const [printingVisitCode, setPrintingVisitCode] = useState<string | null>(null);
  const [assigningVisitTarget, setAssigningVisitTarget] = useState<AssignRoomVisitTarget | null>(
    null,
  );

  const queueEntries = (queueQuery.data?.servicePoints ?? []).flatMap((sp) =>
    (sp.entries ?? []).map(toQueueEntry),
  );

  const patientRows = searchQuery.data?.items ?? [];
  const stats: ReceptionStats | null =
    summaryQuery.data ?? searchQuery.data?.dashboardSummary ?? null;
  const appointments = patientRows
    .filter((row) => row.todayAppointment)
    .map((row) => ({
      id: row.todayAppointment!.id,
      time: row.todayAppointment!.time,
      patientName: row.fullName,
      patientCode: row.patientCode,
      doctorName: row.todayAppointment!.doctorName,
      department: row.todayAppointment!.department,
      roomName: row.todayAppointment!.roomName,
      status: row.todayAppointment!.status,
    }));

  const toScheduledCheckIn = (appointmentId: string): ScheduledCheckInAppointment | null => {
    const row = patientRows.find((item) => item.todayAppointment?.id === appointmentId);
    if (!row?.todayAppointment) return null;
    const birthYear = Number.parseInt(row.dateOfBirth.slice(0, 4), 10);
    return {
      id: row.todayAppointment.id,
      appointmentId: Number(row.todayAppointment.id),
      appointmentCode: `#${row.todayAppointment.id}`,
      patientCode: row.patientCode,
      patientName: row.fullName,
      gender: row.gender,
      birthYear: Number.isFinite(birthYear) ? birthYear : 0,
      identityCard: row.identityCard,
      phoneNumber: row.phoneNumber,
      doctorName: row.todayAppointment.doctorName,
      department: row.todayAppointment.department,
      roomName: row.todayAppointment.roomName,
      time: row.todayAppointment.time,
      date: todayLabel,
      status: row.todayAppointment.status === 'CHECKED_IN' ? 'CHECKED_IN' : 'BOOKED',
    };
  };

  const openCheckInResult = async (appointmentId: string) => {
    setResultError(null);
    try {
      setSelectedCheckInResult(await fetchCheckInResult(appointmentId));
    } catch (error) {
      setResultError(getErrorMessage(error));
    }
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <ReceptionSidebar
        activeItem="dashboard"
        onItemSelect={(item) => {
          if (item === 'patients') navigate('/reception/patients');
          if (item === 'identity-claims') navigate('/reception/identity-claims');
          if (item === 'queue') navigate('/reception/queue');
        }}
      />

      <div className="flex-1 flex flex-col min-w-0">
        {/* Top bar */}
        <header className="h-16 bg-white border-b border-slate-200 px-6 flex items-center justify-between">
          <div>
            <div className="text-sm font-bold text-slate-900">Tổng quan quầy tiếp đón</div>
            <div className="text-[11px] text-slate-500">Ca sáng • Hôm nay, {todayLabel}</div>
          </div>
          <div className="flex items-center gap-3">
            {canCheckInOrQueue && (
              <Button variant="primary" size="sm" onClick={() => setIsCrossDayResumeOpen(true)}>
                <CalendarClock className="w-3.5 h-3.5" aria-hidden="true" />
                <span>Tiếp tục lượt khám (Qua ngày)</span>
              </Button>
            )}
            <button
              type="button"
              aria-label="Thông báo"
              className="p-2 rounded-lg text-slate-400 hover:text-slate-700 hover:bg-slate-100 transition-colors relative"
              title="Thông báo"
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
              <div className="w-9 h-9 rounded-full bg-emerald-100 border border-emerald-200 text-emerald-700 flex items-center justify-center font-bold text-xs">
                {(user?.fullName ?? 'LT').slice(0, 2).toUpperCase()}
              </div>
              <div className="text-right">
                <div className="text-xs font-bold text-slate-800">
                  {user?.fullName ?? 'Nhân viên tiếp đón'}
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
          {updateNotice && (
            <div
              role="status"
              className="rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm font-semibold text-emerald-700"
            >
              {updateNotice}
            </div>
          )}

          {resultError ? <Alert tone="error">{resultError}</Alert> : null}
          {searchQuery.isError ? (
            <Alert tone="error">{getErrorMessage(searchQuery.error)}</Alert>
          ) : null}

          <ReceptionStatCards
            stats={stats}
            isLoading={stats === null && summaryQuery.isPending}
            isError={summaryQuery.isError && stats === null}
          />

          <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
            {/* Queue preview */}
            <Card className="xl:col-span-2">
              <CardHeader className="flex items-center justify-between">
                <div>
                  <div className="text-sm font-bold text-slate-900">Hàng đợi hiện tại</div>
                  <div className="text-[11px] text-slate-500">
                    {queueEntries.length} lượt • cập nhật theo thời gian thực
                  </div>
                </div>
                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    onClick={() => navigate('/reception/queue')}
                    className="text-xs font-semibold text-primary hover:underline"
                  >
                    Xem theo phòng khám
                  </button>
                  <Badge tone="sky">Trực tiếp</Badge>
                </div>
              </CardHeader>
              <CardContent className="p-0 px-6 pb-4">
                <QueuePreviewTable
                  entries={queueEntries}
                  onSelectPatient={setSelectedPatientCode}
                  onAssignRoom={
                    canAssignVisit
                      ? (entry) => {
                          setUpdateNotice(null);
                          setAssigningVisitTarget({
                            visitIdOrCode: entry.visitCode ?? entry.ticketNo,
                            patientCode: entry.patientCode,
                            patientName: entry.patientName,
                            currentRoomCode: entry.room,
                            priority: entry.priority,
                            assignedDoctorName: entry.doctorName,
                            departmentName: entry.departmentName,
                          });
                        }
                      : undefined
                  }
                  onTransferRoom={
                    canAssignVisit
                      ? (entry) => {
                          setUpdateNotice(null);
                          setTransferringTarget({
                            visitIdOrCode: entry.visitCode ?? entry.ticketNo,
                            patientCode: entry.patientCode,
                            patientName: entry.patientName,
                            currentRoomCode: entry.room,
                            ticketNo: entry.ticketNo,
                          });
                        }
                      : undefined
                  }
                  onPrintTicket={(entry) => setPrintingVisitCode(entry.visitCode ?? entry.ticketNo)}
                  onCollectCash={
                    canCollectCash
                      ? (entry) => {
                          setUpdateNotice(null);
                          setCashTarget({
                            visitIdOrCode: entry.visitCode ?? entry.ticketNo,
                            patientName: entry.patientName,
                            patientCode: entry.patientCode,
                          });
                        }
                      : undefined
                  }
                />
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
                    actions={QUICK_ACTIONS_MOCK.filter(
                      (action) =>
                        (action.id !== 'transfer-room' && action.id !== 'assign-room') ||
                        canAssignVisit,
                    )}
                    onAction={(id) => {
                      if (id === 'check-in') {
                        const next = appointments.find((item) => item.status === 'BOOKED');
                        if (next) setSelectedCheckIn(toScheduledCheckIn(next.id));
                      } else if (id === 'new-patient') {
                        setCreateOpen(true);
                      } else if (id === 'walk-in') {
                        navigate('/reception/walk-in');
                      } else if (id === 'search') {
                        navigate('/reception/patients');
                      } else if (id === 'update-patient') {
                        setUpdateNotice(null);
                        navigate('/reception/patients?action=update');
                      } else if (id === 'transfer-room') {
                        setUpdateNotice(null);
                        const candidate =
                          queueEntries.find(
                            (q) => q.status === 'WAITING' || q.status === 'CALLED',
                          ) ?? queueEntries[0];
                        if (candidate) {
                          setTransferringTarget({
                            visitIdOrCode: candidate.visitCode ?? candidate.ticketNo,
                            patientCode: candidate.patientCode,
                            patientName: candidate.patientName,
                            currentRoomCode: candidate.room,
                            ticketNo: candidate.ticketNo,
                          });
                        }
                      } else if (id === 'print-ticket') {
                        setUpdateNotice(null);
                        setPrintingVisitCode(
                          queueEntries[0]?.visitCode ?? queueEntries[0]?.ticketNo ?? null,
                        );
                      } else if (id === 'assign-room') {
                        setUpdateNotice(null);
                        const candidate =
                          queueEntries.find(
                            (q) => q.status === 'WAITING' || q.status === 'CALLED',
                          ) ?? queueEntries[0];
                        if (candidate) {
                          setAssigningVisitTarget({
                            visitIdOrCode: candidate.visitCode ?? candidate.ticketNo,
                            patientCode: candidate.patientCode,
                            patientName: candidate.patientName,
                            currentRoomCode: candidate.room,
                            priority: candidate.priority,
                            assignedDoctorName: candidate.doctorName,
                            departmentName: candidate.departmentName,
                          });
                        }
                      }
                    }}
                  />
                </CardContent>
              </Card>

              <Card>
                <CardHeader className="flex items-center justify-between">
                  <div className="text-sm font-bold text-slate-900">Lịch hẹn sắp tới</div>
                  <Badge tone="amber">{appointments.length} ca</Badge>
                </CardHeader>
                <CardContent className="p-0">
                  <ul className="divide-y divide-slate-100">
                    {appointments.map((ap) => (
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
                        <Badge tone={APPOINTMENT_STATUS_LABEL[ap.status]?.tone ?? 'slate'}>
                          {APPOINTMENT_STATUS_LABEL[ap.status]?.label ?? ap.status}
                        </Badge>
                        {ap.status === 'BOOKED' && (
                          <button
                            type="button"
                            onClick={() => setSelectedCheckIn(toScheduledCheckIn(ap.id))}
                            className="px-2.5 py-1 rounded-lg bg-emerald-600 hover:bg-emerald-700 text-white font-semibold text-[11px] shadow-2xs transition-colors shrink-0"
                          >
                            Check-in
                          </button>
                        )}
                        {ap.status === 'CHECKED_IN' && (
                          <button
                            type="button"
                            onClick={() => void openCheckInResult(ap.id)}
                            className="px-2.5 py-1 rounded-lg bg-sky-50 hover:bg-sky-100 text-sky-700 border border-sky-200 font-semibold text-[11px] transition-colors shrink-0"
                          >
                            Xem kết quả
                          </button>
                        )}
                      </li>
                    ))}
                  </ul>
                </CardContent>
              </Card>
            </div>
          </div>
        </main>

        <PatientDetailModal
          patient={selectedPatient}
          onClose={() => setSelectedPatientCode(null)}
        />
      </div>

      <CheckInModal
        isOpen={!!selectedCheckIn}
        onClose={() => setSelectedCheckIn(null)}
        appointment={selectedCheckIn}
        onSubmit={(form) =>
          checkInMutation.mutateAsync({
            appointmentIdOrCode: String(
              selectedCheckIn?.appointmentId ?? selectedCheckIn?.id ?? '',
            ),
            form,
          })
        }
        onSuccess={() => {
          void searchQuery.refetch();
        }}
        onGoToRouting={(result) => {
          setSelectedCheckIn(null);
          if (result.visitCode) {
            navigate(`/reception/routing?visitId=${encodeURIComponent(result.visitCode)}`);
          }
        }}
      />
      <CheckInResultModal
        isOpen={!!selectedCheckInResult}
        onClose={() => setSelectedCheckInResult(null)}
        result={selectedCheckInResult}
      />
      <CreatePatientModal
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        onSubmit={async (form) => {
          try {
            const created = await createPatientMutation.mutateAsync(form);
            return created;
          } catch (error) {
            setResultError(getErrorMessage(error));
            throw error;
          }
        }}
      />

      {editingPatient && (
        <UpdatePatientModal
          open
          patient={editingPatient}
          onClose={() => setEditingPatient(null)}
          onSubmit={async (code, values) => {
            try {
              await updatePatientMutation.mutateAsync({ idOrCode: code, values });
              setEditingPatient(null);
              setUpdateNotice(`Đã cập nhật hồ sơ ${code} — ${values.fullName}`);
            } catch (error) {
              setUpdateNotice(null);
              setResultError(getErrorMessage(error));
              setEditingPatient(null);
            }
          }}
        />
      )}

      {transferringTarget && (
        <TransferVisitRoomModal
          open
          target={transferringTarget}
          onClose={() => setTransferringTarget(null)}
          onTransferred={(result) => {
            void queueQuery.refetch();
            setTransferringTarget(null);
            setUpdateNotice(
              `Đã chuyển phòng khám cho ${transferringTarget.patientName} từ ${result.previousRoomCode} sang ${result.currentRoomCode} (${result.currentRoomName})`,
            );
          }}
        />
      )}

      {printingVisitCode && (
        <PrintExaminationTicketModal
          key={printingVisitCode}
          open
          initialVisitCode={printingVisitCode}
          onClose={() => setPrintingVisitCode(null)}
          onPrinted={(ticket) => {
            setUpdateNotice(
              `Đã in phiếu khám ${ticket.ticketNo} (${ticket.visitCode}) cho bệnh nhân ${ticket.patientName}`,
            );
          }}
        />
      )}

      {assigningVisitTarget && (
        <AssignExaminationRoomModal
          open
          target={assigningVisitTarget}
          onClose={() => setAssigningVisitTarget(null)}
          onAssigned={(result) => {
            void queueQuery.refetch();
            setAssigningVisitTarget(null);
            setUpdateNotice(
              `Đã phân buồng khám ${result.roomCode} (${result.roomName}) cho lượt khám ${result.visitCode}`,
            );
          }}
        />
      )}

      {cashTarget && (
        <ReceptionCashCollectionModal
          open
          target={cashTarget}
          onClose={() => setCashTarget(null)}
          onCollected={(receipt) => {
            void queueQuery.refetch();
            setUpdateNotice(
              `Đã thu tiền mặt ${receipt.paymentCode} (${receipt.amount.toLocaleString('vi-VN')} VNĐ) cho bệnh nhân ${cashTarget.patientName}`,
            );
          }}
        />
      )}

      {isCrossDayResumeOpen && (
        <ReceptionVisitResumeModal
          open
          onClose={() => setIsCrossDayResumeOpen(false)}
          onResumed={(res) => {
            void queueQuery.refetch();
            setUpdateNotice(
              `Đã xác nhận có mặt và tiếp tục lượt khám ${res.visitCode} (${res.target})`,
            );
          }}
          onDeferred={(res) => {
            void queueQuery.refetch();
            setUpdateNotice(`Đã ghi nhận tạm hoãn / rời viện cho lượt khám ${res.visitCode}`);
          }}
        />
      )}
    </div>
  );
}
