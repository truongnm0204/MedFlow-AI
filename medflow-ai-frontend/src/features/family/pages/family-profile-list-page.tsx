import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { AlertCircle, ChevronRight, Home, Plus, RefreshCw } from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';

import { AddFamilyMemberModal } from '../components/add-family-member-modal';
import { EditFamilyMemberModal } from '../components/edit-family-member-modal';
import { FamilyEmptySlotCard } from '../components/family-empty-slot-card';
import { FamilyMemberCard } from '../components/family-member-card';
import { RemoveFamilyMemberModal } from '../components/remove-family-member-modal';
import { useFamilyMembers } from '../hooks/use-family-members';
import type { FamilyMember } from '../types/family.types';

export function FamilyProfileListPage() {
  const navigate = useNavigate();
  const { data: members = [], isPending, isError, error, refetch } = useFamilyMembers();
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [editingMember, setEditingMember] = useState<FamilyMember | null>(null);
  const [removingMember, setRemovingMember] = useState<FamilyMember | null>(null);

  const handleBookAppointment = (member: FamilyMember) => {
    navigate(`/booking?patientId=${encodeURIComponent(String(member.memberPatientId))}`);
  };

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 pb-16 pt-5">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <nav aria-label="Breadcrumb" className="mb-6 flex items-center gap-2 text-xs text-sub">
            <Link to="/" className="flex items-center gap-1.5 transition-colors hover:text-primary">
              <Home className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Trang chủ</span>
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
            <Link to="/profile" className="transition-colors hover:text-primary">
              Tài khoản cá nhân
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
            <span className="font-semibold text-ink">Hồ sơ người thân</span>
          </nav>

          <div className="mb-8 flex flex-wrap items-end justify-between gap-4">
            <div>
              <span className="inline-flex items-center rounded-full border border-sky-200/80 bg-sky-50 px-3 py-1 text-[11px] font-bold uppercase tracking-wider text-sky-700">
                Quản lý hồ sơ y tế gia đình
              </span>
              <h1 className="mt-2.5 text-2xl font-bold tracking-tight text-ink sm:text-3xl">
                Quản lý Hồ sơ Người thân trong Gia đình
              </h1>
              <p className="mt-1.5 max-w-2xl text-xs leading-relaxed text-sub sm:text-sm">
                Quản lý tập trung hồ sơ khám bệnh ngoại trú của các thành viên trong gia đình để đặt
                lịch hẹn và tra cứu kết quả thuận tiện •{' '}
                <strong className="font-mono text-ink">{members.length}</strong>{' '}
                <span>hồ sơ thành viên</span>
              </p>
            </div>

            <button
              type="button"
              onClick={() => setIsAddModalOpen(true)}
              className="inline-flex items-center gap-2 rounded-xl bg-primary px-5 py-2.5 text-xs font-bold text-white shadow-sm transition-all hover:bg-primary-dark active:scale-[0.99]"
            >
              <Plus className="h-4 w-4" aria-hidden="true" />
              <span>Thêm người thân mới</span>
            </button>
          </div>

          {isPending && (
            <div
              role="status"
              className="rounded-2xl border border-border bg-white p-10 text-center shadow-xs"
            >
              <div className="mx-auto h-10 w-10 animate-spin rounded-full border-4 border-slate-200 border-t-primary" />
              <p className="mt-4 text-sm font-semibold text-ink">
                Đang tải danh sách hồ sơ người thân...
              </p>
            </div>
          )}

          {!isPending && isError && (
            <div
              role="alert"
              className="rounded-2xl border border-rose-200 bg-white p-8 text-center shadow-xs"
            >
              <AlertCircle className="mx-auto h-8 w-8 text-rose-600" aria-hidden="true" />
              <h2 className="mt-3 text-base font-bold text-ink">
                Không thể tải danh sách hồ sơ người thân
              </h2>
              <p className="mx-auto mt-1 max-w-md text-xs text-sub">
                {error?.message ?? 'Máy chủ đang bận hoặc phiên đăng nhập đã hết hạn.'}
              </p>
              <button
                type="button"
                onClick={() => void refetch()}
                className="mt-4 inline-flex items-center gap-1.5 rounded-xl bg-primary px-4 py-2 text-xs font-semibold text-white hover:bg-primary-dark"
              >
                <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Thử lại</span>
              </button>
            </div>
          )}

          {!isPending && !isError && (
            <div className="grid grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-3">
              {members.map((member) => (
                <FamilyMemberCard
                  key={member.id}
                  member={member}
                  onBookAppointment={handleBookAppointment}
                  onEditMember={(target) => setEditingMember(target)}
                  onRemoveMember={(target) => setRemovingMember(target)}
                />
              ))}

              <FamilyEmptySlotCard onAddMember={() => setIsAddModalOpen(true)} />
            </div>
          )}
        </div>
      </main>

      <PatientFooter />

      <AddFamilyMemberModal
        open={isAddModalOpen}
        onClose={() => setIsAddModalOpen(false)}
        onAddMember={() => void refetch()}
      />

      {editingMember && (
        <EditFamilyMemberModal
          open={Boolean(editingMember)}
          onClose={() => setEditingMember(null)}
          member={editingMember}
          onSave={() => void refetch()}
        />
      )}

      {removingMember && (
        <RemoveFamilyMemberModal
          open={Boolean(removingMember)}
          onClose={() => setRemovingMember(null)}
          member={removingMember}
          onSuccess={() => {
            setRemovingMember(null);
            void refetch();
          }}
        />
      )}
    </div>
  );
}
