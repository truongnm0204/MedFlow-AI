import { useState } from 'react';
import { Link } from 'react-router-dom';
import { ChevronRight, Home, Plus } from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';

import { AddFamilyMemberModal } from '../components/add-family-member-modal';
import { EditFamilyMemberModal } from '../components/edit-family-member-modal';
import { FamilyEmptySlotCard } from '../components/family-empty-slot-card';
import { FamilyMemberCard } from '../components/family-member-card';
import { RemoveFamilyMemberModal } from '../components/remove-family-member-modal';
import { getStoredFamilyMembers } from '../data/family-mock';
import type { FamilyMember } from '../types/family.types';

export function FamilyProfileListPage() {
  const [members, setMembers] = useState<FamilyMember[]>(() => getStoredFamilyMembers());
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [editingMember, setEditingMember] = useState<FamilyMember | null>(null);
  const [removingMember, setRemovingMember] = useState<FamilyMember | null>(null);

  const handleAddMember = (_newMember: FamilyMember) => {
    setMembers(getStoredFamilyMembers());
  };

  const handleSaveMember = (_updatedMember: FamilyMember) => {
    setMembers(getStoredFamilyMembers());
  };

  const handleBookAppointment = (member: FamilyMember) => {
    window.location.assign(`/#booking?patient=${encodeURIComponent(member.fullName)}`);
  };

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 pt-5 pb-16">
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
            <Link to="/profile" className="transition-colors hover:text-sky-600">
              Tài khoản cá nhân
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" />
            <span className="font-semibold text-slate-900">Hồ sơ người thân</span>
          </nav>

          {/* Heading & Stat Widget Bar */}
          <div className="mt-6 flex flex-wrap items-end justify-between gap-4 border-b border-slate-200/80 pb-6">
            <div>
              <h1 className="text-xl font-bold tracking-tight text-slate-900 sm:text-2xl">
                Quản lý Hồ sơ Người thân trong Gia đình
              </h1>
              <p className="mt-1 max-w-2xl text-xs leading-relaxed text-slate-500">
                Quản lý danh sách thành viên gia đình được ủy quyền đặt lịch khám và tra cứu kết quả
                khám ngoại trú nhanh chóng, chính xác.
              </p>
            </div>

            <div className="flex items-center gap-3">
              <div className="flex items-center gap-3 rounded-xl border border-slate-200 bg-white px-4 py-2 shadow-2xs">
                <div className="flex items-center gap-2">
                  <span className="h-2 w-2 rounded-full bg-emerald-500" />
                  <span className="text-xs text-slate-500">Đang quản lý:</span>
                </div>
                <div className="flex items-baseline gap-1">
                  <span className="font-bold text-slate-900">{members.length}</span>
                  <span className="text-[11px] text-slate-400">hồ sơ thành viên</span>
                </div>
              </div>

              <button
                type="button"
                onClick={() => setIsAddModalOpen(true)}
                className="flex items-center gap-1.5 rounded-xl bg-sky-600 px-4 py-2.5 text-xs font-bold text-white shadow-sm transition-all hover:bg-sky-700 active:scale-[0.99]"
              >
                <Plus className="h-4 w-4 stroke-[2.5]" />
                Thêm người thân mới
              </button>
            </div>
          </div>

          {/* Members 2-Column Grid */}
          <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-2">
            {members.map((member) => (
              <FamilyMemberCard
                key={member.id}
                member={member}
                onBookAppointment={handleBookAppointment}
                onEditMember={(m) => setEditingMember(m)}
                onRemoveMember={(m) => setRemovingMember(m)}
              />
            ))}
          </div>

          {/* Empty Slot Card to add more */}
          <FamilyEmptySlotCard onAddMember={() => setIsAddModalOpen(true)} />
        </div>
      </main>

      <PatientFooter />

      <AddFamilyMemberModal
        open={isAddModalOpen}
        onClose={() => setIsAddModalOpen(false)}
        onAddMember={handleAddMember}
      />

      {editingMember && (
        <EditFamilyMemberModal
          open={Boolean(editingMember)}
          onClose={() => setEditingMember(null)}
          member={editingMember}
          onSave={handleSaveMember}
        />
      )}

      {removingMember && (
        <RemoveFamilyMemberModal
          open={Boolean(removingMember)}
          onClose={() => setRemovingMember(null)}
          member={removingMember}
          onSuccess={() => {
            setMembers(getStoredFamilyMembers());
            setRemovingMember(null);
          }}
        />
      )}
    </div>
  );
}
