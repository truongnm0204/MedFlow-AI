import { useState } from 'react'

import { AppHeader } from '@/shared/components/layout/app-header'
import { PatientFooter } from '@/shared/components/layout/patient-footer'

import { AiTriageBanner } from '../components/ai-triage-banner'
import { DoctorsSection } from '../components/doctors-section'
import { GuaranteeSection } from '../components/guarantee-section'
import { HeroSection } from '../components/hero-section'
import { JourneySection } from '../components/journey-section'
import { SpecialtiesSection } from '../components/specialties-section'
import { WorkbenchPreviewModal } from '../components/workbench-preview-modal'

export function HomePage() {
  const [showWorkbenchModal, setShowWorkbenchModal] = useState(false)

  return (
    <div className="min-h-screen flex flex-col bg-[#F8FAFC] text-slate-800 antialiased font-sans">
      <AppHeader />

      {/* Quick HIS Staff View Switcher Banner */}
      <div className="bg-sky-50 border-b border-sky-100 py-2 px-4 text-center text-xs text-sky-800 flex items-center justify-center gap-3">
        <span>Đang xem giao diện: <strong>Cổng Bệnh nhân Ngoại trú (Patient Web Portal)</strong></span>
        <button
          type="button"
          onClick={() => setShowWorkbenchModal(true)}
          className="px-2.5 py-0.5 rounded-md bg-white border border-sky-300 font-semibold text-sky-700 hover:bg-sky-100 text-xs shadow-2xs transition-colors"
        >
          Mở Bàn làm việc Bác sĩ (HIS Workbench Shell)
        </button>
      </div>

      <main className="flex-1">
        <HeroSection />
        <AiTriageBanner />
        <SpecialtiesSection />
        <DoctorsSection />
        <JourneySection />
        <GuaranteeSection />
      </main>

      <PatientFooter />

      <WorkbenchPreviewModal open={showWorkbenchModal} onClose={() => setShowWorkbenchModal(false)} />
    </div>
  )
}
