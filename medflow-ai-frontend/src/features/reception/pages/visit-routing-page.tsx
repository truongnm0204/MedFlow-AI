import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';
import { AssignDoctorPanel } from '../components/assign-doctor-panel';
import { ReceptionSidebar } from '../components/reception-sidebar';
import { ASSIGN_DOCTOR_MOCK_DOCTORS, ASSIGN_DOCTOR_VISIT_MOCK } from '../data/assign-doctor-mock';
import type { AssignDoctorPayload } from '../types/assign-doctor.types';

export function VisitRoutingPage() {
  const navigate = useNavigate();
  const [selectedDoctorId, setSelectedDoctorId] = useState<string | null>(null);
  const [assigned, setAssigned] = useState<AssignDoctorPayload | null>(null);

  const handleConfirm = () => {
    const doctor = ASSIGN_DOCTOR_MOCK_DOCTORS.find((d) => d.id === selectedDoctorId);
    if (!doctor) return;
    setAssigned({
      visitId: ASSIGN_DOCTOR_VISIT_MOCK.visitId,
      doctorId: doctor.id,
      roomName: doctor.roomName,
    });
  };

  const assignedDoctor = assigned
    ? ASSIGN_DOCTOR_MOCK_DOCTORS.find((d) => d.id === assigned.doctorId)
    : null;

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <ReceptionSidebar
        activeItem="routing"
        onItemSelect={(item) => {
          if (item === 'dashboard') navigate('/reception');
          if (item === 'patients') navigate('/reception/patients');
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
          {assigned && assignedDoctor && (
            <div
              role="status"
              className="rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700"
            >
              Đã phân bổ {assignedDoctor.fullName} — {assignedDoctor.roomName}
            </div>
          )}
          <AssignDoctorPanel
            patient={ASSIGN_DOCTOR_VISIT_MOCK}
            doctors={ASSIGN_DOCTOR_MOCK_DOCTORS}
            selectedDoctorId={selectedDoctorId}
            onSelectDoctor={setSelectedDoctorId}
            onConfirm={handleConfirm}
          />
        </main>
      </div>
    </div>
  );
}
