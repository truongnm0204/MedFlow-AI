import { useState } from 'react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';

import { AiTriageBanner } from '../components/ai-triage-banner';
import { DoctorsSection } from '../components/doctors-section';
import { GuaranteeSection } from '../components/guarantee-section';
import { HeroSection } from '../components/hero-section';
import { JourneySection } from '../components/journey-section';
import { PatientHomeHub } from '../components/patient-home-hub';
import { SpecialtiesSection } from '../components/specialties-section';
import { WorkbenchPreviewModal } from '../components/workbench-preview-modal';

const STAFF_ROLES: Record<string, true> = {
  DOCTOR: true,
  RECEPTION: true,
  DIAGNOSTIC_STAFF: true,
  PHARMACY_STAFF: true,
  HOSPITAL_ADMIN: true,
  SYSTEM_ADMIN: true,
};

export function HomePage() {
  const [showWorkbenchModal, setShowWorkbenchModal] = useState(false);
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const user = useAuthStore((s) => s.user);

  const userRoles = user?.roles ?? [];
  const canAccessStaffWorkbench = isAuthenticated && userRoles.some((role) => STAFF_ROLES[role]);
  const isPatientPortalView =
    isAuthenticated && (userRoles.length === 0 || userRoles.includes('PATIENT'));

  return (
    <div className="flex min-h-screen flex-col bg-[#F8FAFC] font-sans text-slate-800 antialiased">
      <AppHeader />

      {/* Chỉ hiển thị thanh chuyển đổi HIS Workbench khi user có role nhân viên y tế / quản trị */}
      {canAccessStaffWorkbench && (
        <div className="flex items-center justify-center gap-3 border-b border-sky-100 bg-sky-50 px-4 py-2 text-center text-xs text-sky-800">
          <span>
            Đang xem giao diện: <strong>Cổng Bệnh nhân Ngoại trú (Patient Web Portal)</strong>
          </span>
          <button
            type="button"
            onClick={() => setShowWorkbenchModal(true)}
            className="rounded-md border border-sky-300 bg-white px-2.5 py-0.5 text-xs font-semibold text-sky-700 shadow-2xs transition-colors hover:bg-sky-100"
          >
            Mở Bàn làm việc Bác sĩ (HIS Workbench Shell)
          </button>
        </div>
      )}

      <main className="flex-1">
        {isPatientPortalView && <PatientHomeHub user={user} />}
        <HeroSection />
        <AiTriageBanner />
        <SpecialtiesSection />
        <DoctorsSection />
        <JourneySection />
        <GuaranteeSection />
      </main>

      <PatientFooter />

      {canAccessStaffWorkbench && (
        <WorkbenchPreviewModal
          open={showWorkbenchModal}
          onClose={() => setShowWorkbenchModal(false)}
        />
      )}
    </div>
  );
}
