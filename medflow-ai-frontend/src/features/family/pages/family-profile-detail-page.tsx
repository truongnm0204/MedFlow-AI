import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { AlertCircle, ArrowLeft, ChevronRight, Home, RefreshCw } from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';

import { EditFamilyMemberModal } from '../components/edit-family-member-modal';
import { FamilyMemberContactCard } from '../components/family-member-contact-card';
import { FamilyMemberDetailActions } from '../components/family-member-detail-actions';
import { FamilyMemberDetailSidebar } from '../components/family-member-detail-sidebar';
import { FamilyMemberIdentityCard } from '../components/family-member-identity-card';
import { RemoveFamilyMemberModal } from '../components/remove-family-member-modal';
import { useFamilyMemberDetail } from '../hooks/use-family-member-detail';
import type { FamilyMember } from '../types/family.types';

export function FamilyProfileDetailPage() {
  const navigate = useNavigate();
  const { memberId = 'FAM-002' } = useParams<{ memberId: string }>();
  const { data: member, isPending, isError, error, refetch } = useFamilyMemberDetail(memberId);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isRemoveModalOpen, setIsRemoveModalOpen] = useState(false);

  const handleBookAppointment = (target: FamilyMember) => {
    window.location.assign(`/#booking?patient=${encodeURIComponent(target.fullName)}`);
  };

  const handleDownloadPdf = () => {
    window.print();
  };

  return (
    <div className="flex min-h-screen flex-col bg-[#F8FAFC] font-sans antialiased">
      <AppHeader />

      <main className="flex-1 pt-5 pb-16">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          {/* Breadcrumb */}
          <nav aria-label="Breadcrumb" className="mb-6 flex items-center gap-2 text-xs text-slate-500">
            <Link
              to="/"
              className="flex items-center gap-1.5 transition-colors hover:text-sky-600"
            >
              <Home className="h-3.5 w-3.5" />
              <span>Trang chủ</span>
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" />
            <Link to="/profile" className="transition-colors hover:text-sky-600">
              Tài khoản cá nhân
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" />
            <Link to="/family" className="transition-colors hover:text-sky-600">
              Hồ sơ người thân
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" />
            <span className="font-semibold text-slate-900">
              {member?.fullName ?? 'Chi tiết hồ sơ'}
            </span>
          </nav>

          {/* Loading State */}
          {isPending && (
            <div
              role="status"
              className="rounded-2xl border border-slate-200 bg-white p-10 text-center shadow-xs"
            >
              <div className="mx-auto h-10 w-10 animate-spin rounded-full border-4 border-slate-200 border-t-sky-600" />
              <p className="mt-4 text-sm font-semibold text-slate-700">
                Đang tải hồ sơ người thân...
              </p>
            </div>
          )}

          {/* Error / Not Found State */}
          {!isPending && (isError || !member) && (
            <div
              role="alert"
              className="rounded-2xl border border-rose-200 bg-white p-8 text-center shadow-xs"
            >
              <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-rose-50 text-rose-600">
                <AlertCircle className="h-6 w-6" />
              </div>
              <h1 className="mt-4 text-base font-bold text-slate-900">
                Không tìm thấy hồ sơ người thân
              </h1>
              <p className="mx-auto mt-1 max-w-md text-xs leading-relaxed text-slate-500">
                {error?.message ??
                  'Hồ sơ thành viên không tồn tại hoặc quyền ủy quyền truy cập đã bị thu hồi.'}
              </p>
              <div className="mt-5 flex items-center justify-center gap-3">
                <button
                  type="button"
                  onClick={() => void refetch()}
                  className="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-50"
                >
                  <RefreshCw className="h-3.5 w-3.5" />
                  Thử lại
                </button>
                <Link
                  to="/family"
                  className="inline-flex items-center gap-1.5 rounded-xl bg-sky-600 px-4 py-2 text-xs font-semibold text-white hover:bg-sky-700"
                >
                  <ArrowLeft className="h-3.5 w-3.5" />
                  Quay lại danh sách người thân
                </Link>
              </div>
            </div>
          )}

          {/* Main 2-Column Content */}
          {!isPending && member && (
            <div className="grid grid-cols-1 items-start gap-8 lg:grid-cols-12">
              <aside className="lg:col-span-4 xl:col-span-4">
                <FamilyMemberDetailSidebar
                  member={member}
                  onBookAppointment={handleBookAppointment}
                />
              </aside>

              <section className="lg:col-span-8 xl:col-span-8">
                <FamilyMemberIdentityCard member={member} />
                <FamilyMemberContactCard
                  member={member}
                  onEditContact={() => setIsEditModalOpen(true)}
                />
                <FamilyMemberDetailActions
                  member={member}
                  onBookAppointment={handleBookAppointment}
                  onDownloadPdf={handleDownloadPdf}
                  onEditContact={() => setIsEditModalOpen(true)}
                  onRemoveMember={() => setIsRemoveModalOpen(true)}
                />
              </section>
            </div>
          )}
        </div>
      </main>

      <PatientFooter />

      {member && (
        <>
          <EditFamilyMemberModal
            open={isEditModalOpen}
            onClose={() => setIsEditModalOpen(false)}
            member={member}
            onSave={() => void refetch()}
          />
          <RemoveFamilyMemberModal
            open={isRemoveModalOpen}
            onClose={() => setIsRemoveModalOpen(false)}
            member={member}
            onSuccess={() => {
              setIsRemoveModalOpen(false);
              navigate('/family');
            }}
          />
        </>
      )}
    </div>
  );
}
