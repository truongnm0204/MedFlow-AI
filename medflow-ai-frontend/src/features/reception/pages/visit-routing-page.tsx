import { ArrowLeft, ArrowRight, RefreshCw } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';
import { Spinner } from '@/shared/ui/spinner';

import { AssignDoctorPanel } from '../components/assign-doctor-panel';
import { ReceptionSidebar } from '../components/reception-sidebar';
import { useAssignableDoctors, useAssignDoctorMutation } from '../hooks/use-reception-api';

export function VisitRoutingPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const visitId = searchParams.get('visitId')?.trim() ?? '';

  const doctorsQuery = useAssignableDoctors(visitId);
  const assignDoctor = useAssignDoctorMutation(visitId);
  const [selectedDoctorId, setSelectedDoctorId] = useState<string | null>(null);

  const doctors = doctorsQuery.data?.doctors ?? [];
  const patient = doctorsQuery.data?.patient ?? null;
  const assigned = assignDoctor.data;

  const handleConfirm = () => {
    const doctor = doctors.find((item) => item.id === selectedDoctorId);
    if (!doctor || !visitId) return;
    assignDoctor.mutate({ visitId, doctorId: doctor.id, roomName: doctor.roomName });
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <ReceptionSidebar
        activeItem="routing"
        onItemSelect={(item) => {
          if (item === 'dashboard') navigate('/reception');
          if (item === 'patients') navigate('/reception/patients');
          if (item === 'identity-claims') navigate('/reception/identity-claims');
        }}
      />

      <div className="flex-1 flex flex-col min-w-0">
        <header className="h-16 bg-white border-b border-border px-6 flex items-center gap-3">
          <button
            type="button"
            onClick={() => navigate('/reception')}
            aria-label="Quay lại dashboard tiếp đón"
            className="p-2 rounded-xl text-sub hover:text-navy hover:bg-canvas transition-colors"
          >
            <ArrowLeft className="w-5 h-5" aria-hidden="true" />
          </button>
          <div>
            <div className="text-sm font-bold text-navy">
              Điều phối ca khám &amp; Phân bổ bác sĩ
            </div>
            <div className="text-[11px] text-sub">Receptionist Workbench • SCR-VIS-01</div>
          </div>
        </header>

        <main className="p-6 flex-1 max-w-4xl w-full mx-auto space-y-4">
          {!visitId ? (
            <Alert tone="warning">
              Thiếu mã lượt khám. Hãy check-in bệnh nhân hoặc tạo lượt vãng lai trước khi điều phối.
            </Alert>
          ) : null}

          {doctorsQuery.isPending && visitId ? (
            <div className="flex items-center justify-center gap-2 py-16 text-xs text-sub">
              <Spinner className="h-4 w-4" />
              Đang tải danh sách bác sĩ khả dụng...
            </div>
          ) : null}

          {doctorsQuery.isError ? (
            <div className="space-y-3">
              <Alert tone="error">{getErrorMessage(doctorsQuery.error)}</Alert>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => void doctorsQuery.refetch()}
                loading={doctorsQuery.isFetching}
              >
                <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                Thử lại
              </Button>
            </div>
          ) : null}

          {assigned ? (
            <div
              role="status"
              className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700"
            >
              <span>
                Đã phân bổ {assigned.doctorName} — {assigned.roomName}
              </span>
              <Button
                size="sm"
                onClick={() =>
                  navigate(`/reception/assign-room?visitId=${encodeURIComponent(visitId)}`)
                }
              >
                Gán buồng khám
                <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
              </Button>
            </div>
          ) : null}

          {assignDoctor.isError ? (
            <Alert tone="error">{getErrorMessage(assignDoctor.error)}</Alert>
          ) : null}

          {patient && doctors.length > 0 ? (
            <AssignDoctorPanel
              patient={patient}
              doctors={doctors}
              selectedDoctorId={selectedDoctorId}
              onSelectDoctor={setSelectedDoctorId}
              onConfirm={handleConfirm}
            />
          ) : null}

          {doctorsQuery.isSuccess && doctors.length === 0 ? (
            <Alert tone="info">Chưa có bác sĩ khả dụng cho lượt khám này.</Alert>
          ) : null}

          {assignDoctor.isPending ? (
            <div className="flex items-center gap-2 text-xs text-sub">
              <Spinner className="h-4 w-4" />
              Đang phân bổ bác sĩ...
            </div>
          ) : null}
        </main>
      </div>
    </div>
  );
}
