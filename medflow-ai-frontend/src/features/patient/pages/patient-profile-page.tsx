import { Link } from 'react-router-dom';
import { ChevronRight, Home } from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';

import { PatientContactCard } from '../components/patient-contact-card';
import { PatientMedicalIdentityCard } from '../components/patient-medical-identity-card';
import { PatientProfileActions } from '../components/patient-profile-actions';
import { PatientProfileSidebar } from '../components/patient-profile-sidebar';
import { mockPatientProfile } from '../data/patient-profile.mock';

export function PatientProfilePage() {
  const profile = mockPatientProfile;

  const handleDownloadPdf = () => {
    window.print();
  };

  return (
    <div className="flex min-h-screen flex-col bg-[#F8FAFC] font-sans antialiased">
      <AppHeader />

      <main className="flex-1 pt-5 pb-16">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          {/* Breadcrumb */}
          <nav
            aria-label="Breadcrumb"
            className="mb-6 flex items-center gap-2 text-xs text-slate-500"
          >
            <Link
              to="/"
              className="flex items-center gap-1.5 transition-colors hover:text-sky-600"
            >
              <Home className="h-3.5 w-3.5" />
              <span>Trang chủ</span>
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" />
            <span className="text-slate-500">Tài khoản cá nhân</span>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" />
            <span className="font-semibold text-slate-900">Hồ sơ Bệnh nhân</span>
          </nav>

          {/* 2-Column Responsive Layout */}
          <div className="grid grid-cols-1 items-start gap-8 lg:grid-cols-12">
            {/* Left Sidebar (Overview, Vitals, Shortcuts, Promo) */}
            <aside className="lg:col-span-4 xl:col-span-4">
              <PatientProfileSidebar
                profile={profile}
                onOpenBookingModal={() => window.location.assign('/#booking')}
              />
            </aside>

            {/* Right Main Column (Medical Identity, Contact & Residence, Actions) */}
            <section className="lg:col-span-8 xl:col-span-8">
              <PatientMedicalIdentityCard identity={profile.identity} />
              <PatientContactCard contact={profile.contact} />
              <PatientProfileActions onDownloadPdf={handleDownloadPdf} />
            </section>
          </div>
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
