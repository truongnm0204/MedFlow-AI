import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { ChevronRight, Home, LayoutDashboard } from 'lucide-react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import {
  getRoleDisplayLabel,
  getRoleLandingRoute,
  isStaffRole,
} from '@/features/auth/utils/role-landing';
import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';

import { EditContactModal } from '../components/edit-contact-modal';
import { PatientContactCard } from '../components/patient-contact-card';
import { PatientMedicalIdentityCard } from '../components/patient-medical-identity-card';
import { PatientProfileActions } from '../components/patient-profile-actions';
import { PatientProfileSidebar } from '../components/patient-profile-sidebar';
import { usePatientProfileQuery } from '../hooks/use-patient-profile';
import type { PatientContact, PatientProfile } from '../types/patient-profile.types';

export function PatientProfilePage() {
  const navigate = useNavigate();
  const authUser = useAuthStore((s) => s.user);
  const isStaffAccount = isStaffRole(authUser?.roles) && !authUser?.roles?.includes('PATIENT');
  const roleLabel = getRoleDisplayLabel(authUser?.roles);
  const workbenchRoute = getRoleLandingRoute(authUser?.roles);
  const { data: remoteProfile } = usePatientProfileQuery();
  const [contactOverride, setContactOverride] = useState<PatientContact | null>(null);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);

  const profile: PatientProfile = contactOverride
    ? { ...remoteProfile, contact: contactOverride }
    : remoteProfile;

  const handleSaveContact = (updatedContact: PatientContact) => {
    setContactOverride(updatedContact);
  };

  const handleDownloadPdf = () => {
    window.print();
  };

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 pb-16 pt-5">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          {/* Breadcrumb */}
          <nav
            aria-label="Breadcrumb"
            className="mb-6 flex items-center gap-2 text-xs text-slate-500"
          >
            <Link to="/" className="flex items-center gap-1.5 transition-colors hover:text-sky-600">
              <Home className="h-3.5 w-3.5" />
              <span>Trang chủ</span>
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" />
            <span className="text-slate-500">Tài khoản cá nhân</span>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" />
            <span className="font-semibold text-slate-900">
              {isStaffAccount ? `Hồ sơ ${roleLabel}` : 'Hồ sơ Bệnh nhân'}
            </span>
          </nav>

          {isStaffAccount && (
            <div className="mb-6 flex flex-wrap items-center justify-between gap-4 rounded-2xl border border-sky-200/80 bg-sky-50/70 px-5 py-4">
              <div>
                <p className="text-xs font-bold uppercase tracking-wider text-sky-700">
                  Tài khoản nhân sự nội bộ • {roleLabel}
                </p>
                <p className="mt-1 text-xs text-slate-600">
                  Đang hiển thị thông tin tài khoản nhân sự{' '}
                  <span className="font-semibold text-slate-900">
                    {authUser?.fullName ?? authUser?.email}
                  </span>
                  {authUser?.staffCode ? ` (Mã NV: ${authUser.staffCode})` : ''}.
                </p>
              </div>
              <Link
                to={workbenchRoute}
                className="inline-flex items-center gap-2 rounded-xl bg-primary px-4 py-2 text-xs font-semibold text-white shadow-xs transition-colors hover:bg-primary-dark"
              >
                <LayoutDashboard className="h-4 w-4" aria-hidden="true" />
                <span>Mở Bàn làm việc ({roleLabel})</span>
              </Link>
            </div>
          )}
          {/* 2-Column Responsive Layout */}
          <div className="grid grid-cols-1 items-start gap-8 lg:grid-cols-12">
            {/* Left Sidebar (Overview, Vitals, Shortcuts, Promo) */}
            <aside className="lg:col-span-4 xl:col-span-4">
              <PatientProfileSidebar
                profile={profile}
                onOpenBookingModal={() => navigate('/booking')}
              />
            </aside>

            {/* Right Main Column (Medical Identity, Contact & Residence, Actions) */}
            <section className="lg:col-span-8 xl:col-span-8">
              <PatientMedicalIdentityCard identity={profile.identity} />
              <PatientContactCard
                contact={profile.contact}
                onEditContact={() => setIsEditModalOpen(true)}
              />
              <PatientProfileActions
                onEditContact={() => setIsEditModalOpen(true)}
                onDownloadPdf={handleDownloadPdf}
              />

              <div className="mt-6 rounded-2xl border border-border bg-white p-5">
                <h2 className="text-sm font-bold text-ink">Đã từng khám tại bệnh viện?</h2>
                <p className="mt-1 text-xs leading-relaxed text-sub">
                  Liên kết tài khoản với hồ sơ bệnh nhân cũ để xem lại lịch sử khám và viện phí. Cần
                  xác thực OTP và nhân viên đối chiếu giấy tờ.
                </p>
                <Link
                  to="/patient/identity-claims"
                  className="mt-3 inline-flex items-center gap-2 rounded-xl bg-primary px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-primary-dark focus-visible:ring-2 focus-visible:ring-primary"
                >
                  <span>Liên kết hồ sơ đã có</span>
                  <ChevronRight className="h-4 w-4" aria-hidden="true" />
                </Link>
              </div>
            </section>
          </div>
        </div>
      </main>

      <PatientFooter />

      <EditContactModal
        open={isEditModalOpen}
        onClose={() => setIsEditModalOpen(false)}
        identity={profile.identity}
        contact={profile.contact}
        onSave={handleSaveContact}
      />
    </div>
  );
}
