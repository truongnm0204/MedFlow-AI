import { ArrowLeft, RefreshCw, UserPlus, Users } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';
import { Spinner } from '@/shared/ui/spinner';

import { CheckInModal } from '../components/checkin-modal';
import { PatientSearchEmptyState } from '../components/patient-search-empty-state';
import { PatientSearchFilterBar } from '../components/patient-search-filter-bar';
import { PatientSearchResultTable } from '../components/patient-search-result-table';
import { ReceptionSidebar } from '../components/reception-sidebar';
import { useCheckInMutation, useReceptionPatientSearch } from '../hooks/use-reception-api';
import type { PatientSearchResultItem, SearchFilterTab } from '../types/patient-search.types';
import type {
  CheckInFormData,
  CheckInSuccessResult,
  ScheduledCheckInAppointment,
} from '../types/checkin.types';

export function PatientSearchPage() {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const [query, setQuery] = useState('');
  const [activeTab, setActiveTab] = useState<SearchFilterTab>('ALL');
  const [checkInTarget, setCheckInTarget] = useState<PatientSearchResultItem | null>(null);
  const [todayLabel] = useState(() => new Date().toLocaleDateString('vi-VN'));

  const searchQuery = useReceptionPatientSearch(query, activeTab);
  const checkIn = useCheckInMutation();

  const patients = searchQuery.data?.items ?? [];
  const counts = searchQuery.data?.counts ?? {
    all: 0,
    hasAppointment: 0,
    checkedIn: 0,
    noAppointment: 0,
  };

  const checkInAppointment = useMemo<ScheduledCheckInAppointment | null>(() => {
    const appointment = checkInTarget?.todayAppointment;
    if (!checkInTarget || !appointment) return null;
    const birthYear = Number.parseInt(checkInTarget.dateOfBirth.slice(0, 4), 10);
    return {
      id: checkInTarget.id,
      appointmentId: Number(appointment.id),
      appointmentCode: `#${appointment.id}`,
      patientCode: checkInTarget.patientCode,
      patientName: checkInTarget.fullName,
      gender: checkInTarget.gender,
      birthYear: Number.isFinite(birthYear) ? birthYear : 0,
      identityCard: checkInTarget.identityCard,
      phoneNumber: checkInTarget.phoneNumber,
      doctorName: appointment.doctorName,
      department: appointment.department,
      roomName: appointment.roomName,
      time: appointment.time,
      date: todayLabel,
      status: appointment.status === 'CHECKED_IN' ? 'CHECKED_IN' : 'BOOKED',
    };
  }, [checkInTarget, todayLabel]);

  const handleSubmitCheckIn = async (form: CheckInFormData): Promise<CheckInSuccessResult> => {
    if (!checkInAppointment) throw new Error('Không xác định được lịch hẹn cần tiếp nhận.');
    const result = await checkIn.mutateAsync({
      appointmentIdOrCode: String(checkInAppointment.appointmentId ?? checkInAppointment.id),
      form,
    });
    await searchQuery.refetch();
    return result;
  };

  const handleCheckInSuccess = (result: CheckInSuccessResult) => {
    setCheckInTarget(null);
    if (result.visitCode) {
      navigate(`/reception/routing?visitId=${encodeURIComponent(result.visitCode)}`);
    }
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <ReceptionSidebar
        activeItem="patients"
        onItemSelect={(item) => {
          if (item === 'dashboard') navigate('/reception');
          if (item === 'patients') navigate('/reception/patients');
          if (item === 'identity-claims') navigate('/reception/identity-claims');
          if (item === 'queue') navigate('/reception/queue');
        }}
      />

      <div className="flex-1 flex flex-col min-w-0">
        <header className="h-16 bg-white border-b border-slate-200 px-6 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={() => navigate('/reception')}
              aria-label="Quay lại dashboard tiếp đón"
              className="p-2 rounded-xl text-slate-500 hover:text-slate-800 hover:bg-slate-100 transition-colors"
            >
              <ArrowLeft className="w-5 h-5" aria-hidden="true" />
            </button>
            <div>
              <div className="text-sm font-bold text-slate-900">
                Tra cứu Bệnh nhân &amp; Lịch hẹn (SCR-REC-02)
              </div>
              <div className="text-[11px] text-slate-500">Hôm nay, {todayLabel}</div>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => navigate('/reception/walk-in')}
              className="gap-2"
            >
              <UserPlus className="w-4 h-4" aria-hidden="true" />
              Khám vãng lai
            </Button>
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

        <main className="flex-1 p-6 space-y-5 max-w-7xl w-full mx-auto">
          <PatientSearchFilterBar
            query={query}
            onQueryChange={setQuery}
            activeTab={activeTab}
            onTabChange={setActiveTab}
            counts={counts}
          />

          {searchQuery.isPending ? (
            <div className="flex items-center justify-center gap-2 py-16 text-xs text-sub">
              <Spinner className="h-4 w-4" />
              Đang tải danh sách bệnh nhân...
            </div>
          ) : null}

          {searchQuery.isError ? (
            <div className="space-y-3">
              <Alert tone="error">{getErrorMessage(searchQuery.error)}</Alert>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => void searchQuery.refetch()}
                loading={searchQuery.isFetching}
              >
                <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                Thử lại
              </Button>
            </div>
          ) : null}

          {!searchQuery.isPending && !searchQuery.isError && patients.length === 0 ? (
            <PatientSearchEmptyState query={query} onClear={() => setQuery('')} />
          ) : null}

          {patients.length > 0 ? (
            <>
              <div className="flex items-center gap-2 text-xs text-sub">
                <Users className="h-4 w-4" aria-hidden="true" />
                {searchQuery.data?.totalCount ?? patients.length} bệnh nhân
              </div>
              <PatientSearchResultTable
                patients={patients}
                onSelectPatient={(patient) =>
                  navigate(
                    `/reception/walk-in?patientCode=${encodeURIComponent(patient.patientCode)}`,
                  )
                }
                onCheckInPatient={(patient) => {
                  if (!patient.todayAppointment) return;
                  setCheckInTarget(patient);
                }}
              />
            </>
          ) : null}
        </main>
      </div>

      <CheckInModal
        isOpen={Boolean(checkInTarget)}
        onClose={() => setCheckInTarget(null)}
        appointment={checkInAppointment}
        onSubmit={handleSubmitCheckIn}
        onSuccess={handleCheckInSuccess}
      />
    </div>
  );
}
