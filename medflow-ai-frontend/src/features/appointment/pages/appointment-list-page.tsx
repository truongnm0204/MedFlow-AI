import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { AlertCircle, ChevronRight, Home, PlusCircle, RotateCcw } from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';

import { AppointmentCard } from '../components/appointment-card';
import { AppointmentEmptyState } from '../components/appointment-empty-state';
import { AppointmentFilterBar } from '../components/appointment-filter-bar';
import { AppointmentQuickViewModal } from '../components/appointment-quick-view-modal';
import { AppointmentSkeletonList } from '../components/appointment-skeleton-list';
import { AppointmentTipBanner } from '../components/appointment-tip-banner';
import { MOCK_APPOINTMENT_MEMBERS } from '../data/appointment-list-mock';
import { useAppointmentListQuery } from '../hooks/use-appointment-list';
import { useBookingStore } from '../store/booking.store';
import type {
  AppointmentListFilterParams,
  AppointmentListItem,
  AppointmentTabGroup,
} from '../types/appointment-list.types';

export function AppointmentListPage() {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const selectService = useBookingStore((s) => s.selectService);
  const selectDoctor = useBookingStore((s) => s.selectDoctor);

  const initialMemberId = searchParams.get('memberId') ?? 'ALL';
  const initialTab = (searchParams.get('tab') as AppointmentTabGroup | null) ?? 'ALL';

  const [filters, setFilters] = useState<AppointmentListFilterParams>({
    memberId: initialMemberId,
    tab: initialTab,
    keyword: '',
    dateFrom: '',
    dateTo: '',
    page: 1,
    pageSize: 10,
  });

  const [selectedTicketAppointment, setSelectedTicketAppointment] =
    useState<AppointmentListItem | null>(null);
  const [actionBannerMessage, setActionBannerMessage] = useState<string | null>(null);

  const { data, isLoading, isError, error, refetch } = useAppointmentListQuery(filters);

  const appointments = data?.items ?? [];
  const memberCounts =
    data?.memberCounts ??
    MOCK_APPOINTMENT_MEMBERS.map((m) => ({
      ...m,
      appointmentCount: 0,
    }));
  const totalAllMembersCount = data?.totalAllMembersCount ?? 0;
  const tabCounts = data?.tabCounts ?? {
    all: 0,
    upcoming: 0,
    completed: 0,
    cancelled: 0,
  };

  const hasActiveFilters = Boolean(
    (filters.memberId && filters.memberId !== 'ALL') ||
    (filters.tab && filters.tab !== 'ALL') ||
    filters.keyword ||
    filters.dateFrom ||
    filters.dateTo,
  );

  const handleSelectMember = (memberId: string) => {
    setActionBannerMessage(null);
    setFilters((prev) => ({
      ...prev,
      memberId,
      page: 1,
    }));
  };

  const handleSelectTab = (tab: AppointmentTabGroup) => {
    setActionBannerMessage(null);
    setFilters((prev) => ({
      ...prev,
      tab,
      page: 1,
    }));
  };

  const handleApplySearchAndDate = ({
    keyword,
    dateFrom,
    dateTo,
  }: {
    keyword: string;
    dateFrom: string;
    dateTo: string;
  }) => {
    setActionBannerMessage(null);
    setFilters((prev) => ({
      ...prev,
      keyword,
      dateFrom,
      dateTo,
      page: 1,
    }));
  };

  const handleResetFilters = () => {
    setActionBannerMessage(null);
    setFilters({
      memberId: 'ALL',
      tab: 'ALL',
      keyword: '',
      dateFrom: '',
      dateTo: '',
      page: 1,
      pageSize: 10,
    });
  };

  const handleReschedule = (appointment: AppointmentListItem) => {
    selectDoctor(appointment.doctorId);
    selectService(appointment.serviceId, appointment.serviceName);
    setActionBannerMessage(
      `Yêu cầu đổi lịch cho mã #${appointment.appointmentCode} đã được ghi nhận. Bạn có thể chọn khung giờ mới hoặc liên hệ Tổng đài 1900 6868.`,
    );
  };

  const handleCancelRequest = (appointment: AppointmentListItem) => {
    setActionBannerMessage(
      `Để hủy lịch hẹn #${appointment.appointmentCode} trước giờ tiếp đón, vui lòng xác nhận qua Tổng đài 1900 6868 hoặc tính năng Hủy lịch trực tuyến (M2).`,
    );
  };

  const handleRebookSpecialty = (appointment: AppointmentListItem) => {
    selectService(appointment.serviceId, appointment.serviceName);
    navigate(`/appointment/doctors?specialty=${encodeURIComponent(appointment.specialtyId)}`);
  };

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 pt-5 pb-16">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          {/* Breadcrumb */}
          <nav aria-label="Breadcrumb" className="mb-4 flex items-center gap-2 text-xs text-sub">
            <Link to="/" className="flex items-center gap-1.5 transition-colors hover:text-primary">
              <Home className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Trang chủ</span>
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
            <span className="font-semibold text-ink">Lịch hẹn của tôi (SCR-APT-08)</span>
          </nav>

          {/* Page Heading & Primary CTA (Figma 17:676) */}
          <div className="mb-6 flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
            <div>
              <h1 className="text-2xl font-bold tracking-tight text-ink sm:text-3xl">
                Lịch Hẹn Của Tôi
              </h1>
              <p className="mt-1 text-xs leading-relaxed text-sub sm:text-sm">
                Quản lý, tra cứu và theo dõi danh sách lịch khám ngoại trú tự nguyện của bạn và
                người thân trong gia đình.
              </p>
            </div>

            <Link
              to="/appointment/services"
              className="inline-flex w-fit items-center gap-2 rounded-xl bg-primary px-4 py-2.5 text-xs font-bold text-white shadow-sm transition-colors hover:bg-primary-dark"
            >
              <PlusCircle className="h-4 w-4" aria-hidden="true" />
              <span>+ Đặt lịch khám mới</span>
            </Link>
          </div>

          {/* Member Filter, Search Form & Status Tabs */}
          <AppointmentFilterBar
            filters={filters}
            memberCounts={memberCounts}
            totalAllMembersCount={totalAllMembersCount}
            tabCounts={tabCounts}
            onSelectMember={handleSelectMember}
            onSelectTab={handleSelectTab}
            onApplySearchAndDate={handleApplySearchAndDate}
            onResetFilters={handleResetFilters}
          />

          {/* Action Info Banner */}
          {actionBannerMessage && (
            <div
              role="status"
              className="mb-5 flex items-center justify-between gap-3 rounded-xl border border-sky-200 bg-sky-50 px-4 py-3 text-xs font-medium text-sky-900"
            >
              <span>{actionBannerMessage}</span>
              <button
                type="button"
                onClick={() => setActionBannerMessage(null)}
                className="font-bold text-primary hover:underline"
              >
                Đóng
              </button>
            </div>
          )}

          {/* Error State */}
          {isError && (
            <div
              role="alert"
              className="mb-6 flex flex-col items-start justify-between gap-3 rounded-2xl border border-rose-200 bg-rose-50 p-5 text-xs text-rose-800 sm:flex-row sm:items-center"
            >
              <div className="flex items-center gap-2.5">
                <AlertCircle className="h-5 w-5 shrink-0 text-rose-600" aria-hidden="true" />
                <div>
                  <p className="font-bold text-rose-900">Không thể tải danh sách lịch hẹn</p>
                  <p className="mt-0.5">
                    {getErrorMessage(error, 'Đã xảy ra lỗi khi kết nối máy chủ lịch hẹn.')}
                  </p>
                </div>
              </div>

              <button
                type="button"
                onClick={() => refetch()}
                className="inline-flex items-center gap-1.5 rounded-xl bg-rose-600 px-3.5 py-2 text-xs font-bold text-white transition-colors hover:bg-rose-700"
              >
                <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Thử lại</span>
              </button>
            </div>
          )}

          {/* Main Appointment Cards List / Loading / Empty */}
          {isLoading ? (
            <AppointmentSkeletonList />
          ) : !isError && appointments.length === 0 ? (
            <AppointmentEmptyState
              hasActiveFilters={hasActiveFilters}
              onResetFilters={handleResetFilters}
            />
          ) : (
            !isError && (
              <div className="space-y-4">
                {appointments.map((apt) => (
                  <AppointmentCard
                    key={apt.id}
                    appointment={apt}
                    onViewTicketModal={(item) => setSelectedTicketAppointment(item)}
                    onViewDetail={(item) => navigate(`/appointments/${item.appointmentCode}`)}
                    onReschedule={handleReschedule}
                    onCancelRequest={handleCancelRequest}
                    onRebookSpecialty={handleRebookSpecialty}
                  />
                ))}
              </div>
            )
          )}

          {/* Bottom Fast Reception Tip Banner (Figma 17:676) */}
          <AppointmentTipBanner />
        </div>
      </main>

      <PatientFooter />

      <AppointmentQuickViewModal
        appointment={selectedTicketAppointment}
        onClose={() => setSelectedTicketAppointment(null)}
      />
    </div>
  );
}
