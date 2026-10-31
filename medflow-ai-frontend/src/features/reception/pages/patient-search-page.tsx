import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft, UserPlus, Users } from 'lucide-react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { useLogout } from '@/features/auth/hooks/use-auth';
import { Button } from '@/shared/ui/button';

import { PatientSearchEmptyState } from '../components/patient-search-empty-state';
import { PatientSearchFilterBar } from '../components/patient-search-filter-bar';
import { PatientSearchResultTable } from '../components/patient-search-result-table';
import { ReceptionSidebar } from '../components/reception-sidebar';
import { PATIENT_SEARCH_MOCK_DATA } from '../data/patient-search-mock';
import type { SearchFilterTab } from '../types/patient-search.types';

export function PatientSearchPage() {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const [query, setQuery] = useState('');
  const [activeTab, setActiveTab] = useState<SearchFilterTab>('ALL');
  const [todayLabel] = useState(() => new Date().toLocaleDateString('vi-VN'));

  // Lọc danh sách bệnh nhân theo từ khóa và tab
  const filteredPatients = useMemo(() => {
    const q = query.trim().toLowerCase();

    return PATIENT_SEARCH_MOCK_DATA.filter((patient) => {
      // Lọc từ khóa
      const matchesQuery =
        !q ||
        patient.fullName.toLowerCase().includes(q) ||
        patient.patientCode.toLowerCase().includes(q) ||
        patient.identityCard.includes(q) ||
        patient.phoneNumber.includes(q);

      if (!matchesQuery) return false;

      // Lọc tab
      if (activeTab === 'HAS_APPOINTMENT') return !!patient.todayAppointment;
      if (activeTab === 'CHECKED_IN') return patient.todayAppointment?.status === 'CHECKED_IN';
      if (activeTab === 'NO_APPOINTMENT') return !patient.todayAppointment;

      return true;
    });
  }, [query, activeTab]);

  // Đếm số lượng cho từng tab
  const counts = useMemo(() => {
    return {
      all: PATIENT_SEARCH_MOCK_DATA.length,
      hasAppointment: PATIENT_SEARCH_MOCK_DATA.filter((p) => !!p.todayAppointment).length,
      checkedIn: PATIENT_SEARCH_MOCK_DATA.filter((p) => p.todayAppointment?.status === 'CHECKED_IN')
        .length,
      noAppointment: PATIENT_SEARCH_MOCK_DATA.filter((p) => !p.todayAppointment).length,
    };
  }, []);

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <ReceptionSidebar
        activeItem="patients"
        onItemSelect={(item) => {
          if (item === 'dashboard') navigate('/reception');
          if (item === 'patients') navigate('/reception/patients');
        }}
      />

      <div className="flex-1 flex flex-col min-w-0">
        {/* Top bar */}
        <header className="h-16 bg-white border-b border-slate-200 px-6 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={() => navigate('/reception')}
              aria-label="Quay lại dashboard tiếp đón"
              className="p-2 rounded-xl text-slate-500 hover:text-slate-800 hover:bg-slate-100 transition-colors"
            >
              <ArrowLeft className="w-5 h-5" aria-hidden="true" />
            </button>
            <div>
              <div className="text-sm font-bold text-slate-900">Tra cứu Bệnh nhân & Tiếp nhận</div>
              <div className="text-[11px] text-slate-500">
                Quầy tiếp đón • Hôm nay, {todayLabel}
              </div>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <div className="flex items-center gap-2.5 pl-3 border-l border-slate-200">
              <div className="w-9 h-9 rounded-full bg-emerald-100 border border-emerald-200 text-emerald-700 flex items-center justify-center font-bold text-xs">
                {(user?.fullName ?? 'LT').slice(0, 2).toUpperCase()}
              </div>
              <div className="text-right">
                <div className="text-xs font-bold text-slate-800">
                  {user?.fullName ?? 'Nhân viên tiếp đón'}
                </div>
                <button
                  type="button"
                  onClick={() => logout.mutate()}
                  className="text-[10px] text-slate-400 hover:text-rose-600 transition-colors"
                >
                  Đăng xuất
                </button>
              </div>
            </div>
          </div>
        </header>

        {/* Main Content */}
        <main className="p-6 space-y-6 flex-1 max-w-7xl w-full mx-auto">
          {/* Header Action Bar */}
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
            <div>
              <h1 className="text-xl font-bold text-slate-900 tracking-tight flex items-center gap-2.5">
                <Users className="w-6 h-6 text-primary" aria-hidden="true" />
                Hồ sơ Bệnh nhân tại quầy
              </h1>
              <p className="text-xs text-slate-500 mt-1">
                Tìm kiếm thông tin theo CCCD/VNeID, Mã bệnh nhân hoặc Số điện thoại để tiếp nhận
                hoặc tạo hồ sơ mới.
              </p>
            </div>

            <Button
              variant="primary"
              size="sm"
              onClick={() => alert('Chức năng Tạo hồ sơ mới (Issue #67) sẽ kết nối tại đây')}
              className="gap-2 shrink-0 shadow-xs"
            >
              <UserPlus className="w-4 h-4" aria-hidden="true" />
              Tạo hồ sơ bệnh nhân mới
            </Button>
          </div>

          {/* Filter Bar */}
          <PatientSearchFilterBar
            query={query}
            onQueryChange={setQuery}
            activeTab={activeTab}
            onTabChange={setActiveTab}
            counts={counts}
          />

          {/* Results Table or Empty State */}
          {filteredPatients.length > 0 ? (
            <PatientSearchResultTable
              patients={filteredPatients}
              onSelectPatient={(p) => {
                alert(`Xem chi tiết hồ sơ bệnh nhân: ${p.fullName} (${p.patientCode})`);
              }}
              onCheckInPatient={(p) => {
                alert(`Chuyển sang luồng Check-in (Issue #71) cho bệnh nhân: ${p.fullName}`);
              }}
            />
          ) : (
            <PatientSearchEmptyState
              query={query}
              onClear={() => {
                setQuery('');
                setActiveTab('ALL');
              }}
            />
          )}
        </main>
      </div>
    </div>
  );
}
