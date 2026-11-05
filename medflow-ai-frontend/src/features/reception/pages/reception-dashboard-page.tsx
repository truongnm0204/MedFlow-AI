import { useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { useLogout } from '@/features/auth/hooks/use-auth';
import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { CheckInModal } from '../components/checkin-modal';
import { CreatePatientModal } from '../components/create-patient-modal';
import { AssignExaminationRoomModal } from '../components/assign-examination-room-modal';
import { PatientDetailModal } from '../components/patient-detail-modal';
import { PrintExaminationTicketModal } from '../components/print-examination-ticket-modal';
import { UpdatePatientModal } from '../components/update-patient-modal';
import { TransferVisitRoomModal } from '../components/transfer-visit-room-modal';
import { QuickActionsPanel } from '../components/quick-actions-panel';
import { SCHEDULED_CHECKIN_APPOINTMENTS_MOCK } from '../data/checkin-mock';
import type { ScheduledCheckInAppointment } from '../types/checkin.types';
import { QueuePreviewTable } from '../components/queue-preview-table';
import { ReceptionSidebar } from '../components/reception-sidebar';
import { ReceptionStatCards } from '../components/reception-stat-cards';
import {
  PATIENT_UPDATE_MOCK,
  QUEUE_ENTRIES_MOCK,
  QUICK_ACTIONS_MOCK,
  RECEPTION_STATS_MOCK,
  TODAY_APPOINTMENTS_MOCK,
} from '../data/reception-mock-data';
import type { CreatePatientFormData, CreatedPatientResult } from '../types/create-patient.types';
import type { AssignRoomVisitTarget } from '../types/assign-examination-room.types';
import { findPatientDetailByCode } from '../data/patient-detail-mock';
import type { QueueEntry, TodayAppointment } from '../types/reception.types';
import type { UpdatePatientFormData } from '../types/update-patient.types';
import type { TransferVisitRoomTarget } from '../types/visit-room-transfer.types';

const APPOINTMENT_STATUS_LABEL: Record<
  TodayAppointment['status'],
  { label: string; tone: 'sky' | 'emerald' | 'rose' | 'slate' }
> = {
  BOOKED: { label: 'Đã đặt', tone: 'sky' },
  CHECKED_IN: { label: 'Đã check-in', tone: 'emerald' },
  CANCELLED: { label: 'Đã hủy', tone: 'rose' },
  NO_SHOW: { label: 'Không đến', tone: 'slate' },
};

// TODO(M2): thay bằng API tạo bệnh nhân khi backend sẵn sàng; hiện sinh mã cục bộ.
async function createPatientLocally(data: CreatePatientFormData): Promise<CreatedPatientResult> {
  const seq = String(Math.floor(Math.random() * 10000)).padStart(4, '0');
  return { patientCode: `BN-${new Date().getFullYear()}-${seq}`, fullName: data.fullName };
}

export function ReceptionDashboardPage() {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();
  const [createOpen, setCreateOpen] = useState(false);
  const [selectedPatientCode, setSelectedPatientCode] = useState<string | null>(null);
  const selectedPatient = selectedPatientCode ? findPatientDetailByCode(selectedPatientCode) : null;
  const [todayLabel] = useState(() => new Date().toLocaleDateString('vi-VN'));
  const [appointments, setAppointments] = useState(TODAY_APPOINTMENTS_MOCK);
  const [selectedCheckIn, setSelectedCheckIn] = useState<ScheduledCheckInAppointment | null>(null);
  const [editingPatient, setEditingPatient] = useState<UpdatePatientFormData | null>(null);
  const [updateNotice, setUpdateNotice] = useState<string | null>(null);
  const [queueEntries, setQueueEntries] = useState<QueueEntry[]>(QUEUE_ENTRIES_MOCK);
  const [transferringTarget, setTransferringTarget] = useState<TransferVisitRoomTarget | null>(
    null,
  );
  const [printingVisitCode, setPrintingVisitCode] = useState<string | null>(null);
  const [assigningVisitTarget, setAssigningVisitTarget] = useState<AssignRoomVisitTarget | null>(
    null,
  );

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <ReceptionSidebar
        activeItem="dashboard"
        onItemSelect={(item) => {
          if (item === 'patients') navigate('/reception/patients');
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

          <ReceptionStatCards stats={RECEPTION_STATS_MOCK} />

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
                  onAssignRoom={(entry) => {
                    setUpdateNotice(null);
                    setAssigningVisitTarget({
                      visitIdOrCode: entry.visitCode ?? 'VIS-202610-0001',
                      patientCode: entry.patientCode,
                      patientName: entry.patientName,
                      currentRoomCode: entry.room,
                      priority: entry.priority,
                      assignedDoctorName: entry.doctorName,
                      departmentName: entry.departmentName,
                    });
                  }}
                  onTransferRoom={(entry) => {
                    setUpdateNotice(null);
                    setTransferringTarget({
                      visitIdOrCode: entry.visitCode ?? 'VIS-202610-0002',
                      patientCode: entry.patientCode,
                      patientName: entry.patientName,
                      currentRoomCode: entry.room,
                      ticketNo: entry.ticketNo,
                    });
                  }}
                  onPrintTicket={(entry) => setPrintingVisitCode(entry.visitCode ?? entry.ticketNo)}
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
                    actions={QUICK_ACTIONS_MOCK}
                    onAction={(id) => {
                      if (id === 'check-in') {
                        setSelectedCheckIn(SCHEDULED_CHECKIN_APPOINTMENTS_MOCK[0]);
                      } else if (id === 'new-patient') {
                        setCreateOpen(true);
                      } else if (id === 'search') {
                        navigate('/reception/patients');
                      } else if (id === 'update-patient') {
                        setUpdateNotice(null);
                        setEditingPatient(PATIENT_UPDATE_MOCK);
                      } else if (id === 'transfer-room') {
                        setUpdateNotice(null);
                        const candidate =
                          queueEntries.find(
                            (q) => q.status === 'WAITING' || q.status === 'CALLED',
                          ) ?? queueEntries[0];
                        if (candidate) {
                          setTransferringTarget({
                            visitIdOrCode: candidate.visitCode ?? 'VIS-202610-0002',
                            patientCode: candidate.patientCode,
                            patientName: candidate.patientName,
                            currentRoomCode: candidate.room,
                            ticketNo: candidate.ticketNo,
                          });
                        }
                      } else if (id === 'print-ticket') {
                        setUpdateNotice(null);
                        setPrintingVisitCode(QUEUE_ENTRIES_MOCK[0]?.visitCode ?? 'VIS-202610-0001');
                      } else if (id === 'assign-room') {
                        setUpdateNotice(null);
                        const candidate =
                          queueEntries.find(
                            (q) => q.status === 'WAITING' || q.status === 'CALLED',
                          ) ?? queueEntries[0];
                        if (candidate) {
                          setAssigningVisitTarget({
                            visitIdOrCode: candidate.visitCode ?? 'VIS-202610-0001',
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
                  <Badge tone="amber">{TODAY_APPOINTMENTS_MOCK.length} ca</Badge>
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
                        <Badge tone={APPOINTMENT_STATUS_LABEL[ap.status].tone}>
                          {APPOINTMENT_STATUS_LABEL[ap.status].label}
                        </Badge>
                        {ap.status === 'BOOKED' && (
                          <button
                            type="button"
                            onClick={() => {
                              const found = SCHEDULED_CHECKIN_APPOINTMENTS_MOCK.find(
                                (item) => item.id === ap.id
                              ) || {
                                id: ap.id,
                                appointmentCode: `APT-2024-${ap.id}`,
                                patientCode: 'BN-2024-0018',
                                patientName: ap.patientName,
                                gender: 'MALE' as const,
                                birthYear: 1988,
                                identityCard: '001088012345',
                                phoneNumber: '0912345678',
                                healthInsuranceNumber: 'DN4010123456789',
                                doctorName: ap.doctorName,
                                department: ap.department,
                                roomName: 'P.102',
                                time: ap.time,
                                date: 'Hôm nay',
                                status: 'BOOKED' as const,
                              };
                              setSelectedCheckIn(found);
                            }}
                            className="px-2.5 py-1 rounded-lg bg-emerald-600 hover:bg-emerald-700 text-white font-semibold text-[11px] shadow-2xs transition-colors shrink-0"
                          >
                            Check-in
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
        onSuccess={() => {
          if (selectedCheckIn) {
            setAppointments((prev) =>
              prev.map((item) =>
                item.id === selectedCheckIn.id ? { ...item, status: 'CHECKED_IN' } : item
              )
            );
          }
        }}
      />
      <CreatePatientModal
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        onSubmit={createPatientLocally}
      />

      {editingPatient && (
        <UpdatePatientModal
          open
          patient={editingPatient}
          onClose={() => setEditingPatient(null)}
          onSubmit={(code, values) => {
            // TODO(M2): goi API cap nhat ho so khi backend san sang.
            setEditingPatient(null);
            setUpdateNotice(`Đã cập nhật hồ sơ ${code} — ${values.fullName}`);
          }}
        />
      )}

      {transferringTarget && (
        <TransferVisitRoomModal
          open
          target={transferringTarget}
          onClose={() => setTransferringTarget(null)}
          onTransferred={(result) => {
            setQueueEntries((prev) =>
              prev.map((entry) =>
                entry.patientCode === transferringTarget.patientCode ||
                entry.visitCode === result.visitCode
                  ? {
                      ...entry,
                      room: result.currentRoomCode,
                      status: entry.status === 'CALLED' ? 'WAITING' : entry.status,
                    }
                  : entry,
              ),
            );
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
            setQueueEntries((prev) =>
              prev.map((item) =>
                item.visitCode === result.visitCode ||
                item.patientCode === assigningVisitTarget.patientCode
                  ? {
                      ...item,
                      room: result.roomCode,
                      priority: result.priority,
                      status: item.status === 'IN_PROGRESS' ? item.status : 'WAITING',
                    }
                  : item,
              ),
            );
            setAssigningVisitTarget(null);
            setUpdateNotice(
              `Đã phân buồng khám ${result.roomCode} (${result.roomName}) cho lượt khám ${result.visitCode}`,
            );
          }}
        />
      )}
    </div>
  );
}
