import { useState } from 'react';
import { Link } from 'react-router-dom';
import { ChevronRight, Home, Plus, ShieldCheck } from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';

import { AddFamilyMemberModal } from '../components/add-family-member-modal';
import { EditFamilyMemberModal } from '../components/edit-family-member-modal';
import { FamilyEmptySlotCard } from '../components/family-empty-slot-card';
import { FamilyMemberCard } from '../components/family-member-card';
import { FamilyPolicyBanner } from '../components/family-policy-banner';
import { mockFamilyMembers } from '../data/family-mock';
import type { FamilyMember } from '../types/family.types';

export function FamilyProfileListPage() {
  const [members, setMembers] = useState<FamilyMember[]>(mockFamilyMembers);
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [editingMember, setEditingMember] = useState<FamilyMember | null>(null);

  const maxSlots = 5;
  const remainingSlots = Math.max(0, maxSlots - members.length);

  const handleAddMember = (newMember: FamilyMember) => {
    if (members.length < maxSlots) {
      setMembers((prev) => [...prev, newMember]);
    }
  };

  const handleSaveMember = (updatedMember: FamilyMember) => {
    setMembers((prev) =>
      prev.map((m) => (m.id === updatedMember.id ? updatedMember : m)),
    );
  };

  const handleBookAppointment = (member: FamilyMember) => {
    window.location.assign(`/#booking?patient=${encodeURIComponent(member.fullName)}`);
  };

  return (
    <div className="flex min-h-screen flex-col bg-[#F8FAFC] font-sans antialiased">
      <AppHeader />

      <main className="flex-1 pt-5 pb-16">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          {/* Top Bar: Breadcrumb + Security Badges */}
          <div className="flex flex-wrap items-center justify-between gap-3">
            <nav aria-label="Breadcrumb" className="flex items-center gap-2 text-xs text-slate-500">
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
              <span className="font-semibold text-slate-900">Hồ sơ người thân</span>
            </nav>

            <div className="flex items-center gap-2.5">
              <span className="inline-flex items-center gap-1.5 rounded-lg border border-emerald-200/80 bg-emerald-50 px-2.5 py-1 text-xs font-semibold text-emerald-700">
                <ShieldCheck className="h-3.5 w-3.5" />
                Chuẩn bảo mật HL7/FHIR Ready
              </span>
              <span className="rounded-lg border border-slate-200 bg-slate-100 px-2.5 py-1 font-mono text-xs font-medium text-slate-500">
                Mã màn hình: SCR-PRO-03
              </span>
            </div>
          </div>

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
                  <span className="font-bold text-slate-900">
                    {members.length}/{maxSlots}
                  </span>
                  <span className="text-[11px] text-slate-400">hồ sơ thành viên</span>
                </div>
              </div>

              {remainingSlots > 0 ? (
                <button
                  type="button"
                  onClick={() => setIsAddModalOpen(true)}
                  className="flex items-center gap-1.5 rounded-xl bg-sky-600 px-4 py-2.5 text-xs font-bold text-white shadow-sm transition-all hover:bg-sky-700 active:scale-[0.99]"
                >
                  <Plus className="h-4 w-4 stroke-[2.5]" />
                  Thêm người thân mới
                </button>
              ) : (
                <span className="rounded-xl border border-slate-200 bg-slate-100 px-3.5 py-2.5 text-xs font-semibold text-slate-500">
                  Đã đạt giới hạn 5/5 hồ sơ
                </span>
              )}
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
              />
            ))}
          </div>

          {/* Empty Slot Card if remaining */}
          {remainingSlots > 0 && (
            <FamilyEmptySlotCard
              remainingSlots={remainingSlots}
              onAddMember={() => setIsAddModalOpen(true)}
            />
          )}

          {/* Bottom Security Policy Banner */}
          <FamilyPolicyBanner />
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
    </div>
  );
}
