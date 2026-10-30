import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import {
  AlertCircle,
  CalendarClock,
  CheckCircle2,
  ListFilter,
  RotateCcw,
  Search,
  UserPlus,
  UserRound,
  XCircle,
} from 'lucide-react';

import { validateAppointmentFilterInput } from '../schemas/appointment-list.schema';
import type {
  AppointmentListFilterParams,
  AppointmentMemberCountItem,
  AppointmentTabCounts,
  AppointmentTabGroup,
} from '../types/appointment-list.types';

interface AppointmentFilterBarProps {
  filters: AppointmentListFilterParams;
  memberCounts: AppointmentMemberCountItem[];
  totalAllMembersCount: number;
  tabCounts: AppointmentTabCounts;
  onSelectMember: (memberId: string) => void;
  onSelectTab: (tab: AppointmentTabGroup) => void;
  onApplySearchAndDate: (params: { keyword: string; dateFrom: string; dateTo: string }) => void;
  onResetFilters: () => void;
}

export function AppointmentFilterBar({
  filters,
  memberCounts,
  totalAllMembersCount,
  tabCounts,
  onSelectMember,
  onSelectTab,
  onApplySearchAndDate,
  onResetFilters,
}: AppointmentFilterBarProps) {
  const [keyword, setKeyword] = useState(filters.keyword ?? '');
  const [dateFrom, setDateFrom] = useState(filters.dateFrom ?? '');
  const [dateTo, setDateTo] = useState(filters.dateTo ?? '');
  const [validationError, setValidationError] = useState<string | null>(null);

  const selectedMember = filters.memberId ?? 'ALL';
  const selectedTab = filters.tab ?? 'ALL';

  const hasActiveSearchOrDate = Boolean(
    filters.keyword ||
    filters.dateFrom ||
    filters.dateTo ||
    selectedMember !== 'ALL' ||
    selectedTab !== 'ALL',
  );

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const validation = validateAppointmentFilterInput({
      ...filters,
      keyword,
      dateFrom,
      dateTo,
    });
    if (!validation.success) {
      setValidationError(validation.errorMessage);
      return;
    }
    setValidationError(null);
    onApplySearchAndDate({
      keyword: validation.data.keyword,
      dateFrom: validation.data.dateFrom,
      dateTo: validation.data.dateTo,
    });
  };

  const handleReset = () => {
    setKeyword('');
    setDateFrom('');
    setDateTo('');
    setValidationError(null);
    onResetFilters();
  };

  return (
    <section
      aria-label="Bộ lọc thành viên và trạng thái lịch hẹn"
      className="mb-6 rounded-2xl border border-border bg-white p-5 shadow-xs"
    >
      {/* Row 1: Member Filter Pills (Figma 17:676) */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <span className="mr-1 text-[11px] font-bold tracking-wider text-sub uppercase">
            XEM LỊCH THEO THÀNH VIÊN:
          </span>

          <button
            type="button"
            onClick={() => onSelectMember('ALL')}
            aria-pressed={selectedMember === 'ALL'}
            className={`inline-flex items-center gap-1.5 rounded-full border px-3.5 py-1.5 text-xs font-semibold transition-colors ${
              selectedMember === 'ALL'
                ? 'border-primary bg-primary text-white shadow-2xs'
                : 'border-border bg-white text-ink hover:border-sky-300 hover:bg-canvas'
            }`}
          >
            <span>Tất cả hồ sơ ({totalAllMembersCount} lịch)</span>
          </button>

          {memberCounts.map((member) => {
            const isSelected = selectedMember === member.id;
            const isZero = member.appointmentCount === 0;
            return (
              <button
                key={member.id}
                type="button"
                onClick={() => onSelectMember(member.id)}
                aria-pressed={isSelected}
                className={`inline-flex items-center gap-1.5 rounded-full border px-3.5 py-1.5 text-xs font-semibold transition-colors ${
                  isSelected
                    ? 'border-primary bg-primary text-white shadow-2xs'
                    : isZero
                      ? 'border-border bg-canvas/60 text-slate-400 hover:border-slate-300 hover:text-sub'
                      : 'border-border bg-white text-ink hover:border-sky-300 hover:bg-canvas'
                }`}
              >
                <UserRound className="h-3.5 w-3.5" aria-hidden="true" />
                <span>
                  {member.fullName} ({member.relationshipLabel} - {member.appointmentCount} lịch)
                </span>
              </button>
            );
          })}
        </div>

        <Link
          to="/family"
          className="inline-flex items-center gap-1.5 text-xs font-bold text-primary transition-colors hover:text-primary-dark"
        >
          <UserPlus className="h-3.5 w-3.5" aria-hidden="true" />
          <span>+ Quản lý người thân</span>
        </Link>
      </div>

      {/* Row 2: Compact Search & Date Range Filter Form (with Zod Validation) */}
      <form
        onSubmit={handleSubmit}
        noValidate
        className="mt-4 flex flex-wrap items-center gap-2.5 border-t border-slate-100 pt-3.5"
      >
        <div className="relative min-w-[220px] flex-1">
          <Search
            className="pointer-events-none absolute top-1/2 left-3 h-3.5 w-3.5 -translate-y-1/2 text-slate-400"
            aria-hidden="true"
          />
          <input
            type="text"
            aria-label="Tìm kiếm lịch hẹn"
            value={keyword}
            onChange={(e) => setKeyword(e.target.value)}
            placeholder="Tìm theo mã phiếu hẹn (#APT-...), bác sĩ, chuyên khoa..."
            className="w-full rounded-xl border border-border bg-canvas/60 py-1.5 pr-3 pl-8 text-xs text-ink placeholder:text-slate-400 focus:border-primary focus:bg-white focus:ring-2 focus:ring-primary/20 focus:outline-none"
          />
        </div>

        <div className="flex items-center gap-1.5">
          <label htmlFor="apt-date-from" className="text-[11px] font-medium text-sub">
            Từ ngày:
          </label>
          <input
            id="apt-date-from"
            type="date"
            aria-label="Từ ngày"
            value={dateFrom}
            onChange={(e) => setDateFrom(e.target.value)}
            className="rounded-xl border border-border bg-canvas/60 px-2.5 py-1.5 text-xs text-ink focus:border-primary focus:bg-white focus:outline-none"
          />
        </div>

        <div className="flex items-center gap-1.5">
          <label htmlFor="apt-date-to" className="text-[11px] font-medium text-sub">
            Đến ngày:
          </label>
          <input
            id="apt-date-to"
            type="date"
            aria-label="Đến ngày"
            value={dateTo}
            onChange={(e) => setDateTo(e.target.value)}
            className="rounded-xl border border-border bg-canvas/60 px-2.5 py-1.5 text-xs text-ink focus:border-primary focus:bg-white focus:outline-none"
          />
        </div>

        <button
          type="submit"
          className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-3.5 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-primary-dark"
        >
          <ListFilter className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Lọc lịch hẹn</span>
        </button>

        {(hasActiveSearchOrDate || validationError) && (
          <button
            type="button"
            onClick={handleReset}
            className="inline-flex items-center gap-1 rounded-xl border border-border bg-white px-3 py-1.5 text-xs font-semibold text-sub transition-colors hover:bg-canvas hover:text-ink"
          >
            <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
            <span>Đặt lại bộ lọc</span>
          </button>
        )}
      </form>

      {validationError && (
        <div
          role="alert"
          className="mt-2.5 flex items-center gap-2 rounded-xl border border-rose-200 bg-rose-50 px-3 py-2 text-xs font-medium text-rose-700"
        >
          <AlertCircle className="h-4 w-4 shrink-0 text-rose-600" aria-hidden="true" />
          <span>{validationError}</span>
        </div>
      )}

      {/* Row 3: Status Tabs (Figma 17:676) */}
      <div
        role="tablist"
        aria-label="Phân loại trạng thái lịch hẹn"
        className="mt-4 flex flex-wrap items-center gap-6 border-t border-slate-100 pt-3"
      >
        <button
          type="button"
          role="tab"
          aria-selected={selectedTab === 'UPCOMING'}
          onClick={() => onSelectTab('UPCOMING')}
          className={`inline-flex items-center gap-1.5 border-b-2 pb-2 text-xs transition-colors ${
            selectedTab === 'UPCOMING'
              ? 'border-primary font-bold text-primary'
              : 'border-transparent font-medium text-sub hover:text-ink'
          }`}
        >
          <CalendarClock className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Sắp tới / Sắp khám ({tabCounts.upcoming})</span>
        </button>

        <button
          type="button"
          role="tab"
          aria-selected={selectedTab === 'COMPLETED'}
          onClick={() => onSelectTab('COMPLETED')}
          className={`inline-flex items-center gap-1.5 border-b-2 pb-2 text-xs transition-colors ${
            selectedTab === 'COMPLETED'
              ? 'border-primary font-bold text-primary'
              : 'border-transparent font-medium text-sub hover:text-ink'
          }`}
        >
          <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Đã hoàn thành ({tabCounts.completed})</span>
        </button>

        <button
          type="button"
          role="tab"
          aria-selected={selectedTab === 'CANCELLED'}
          onClick={() => onSelectTab('CANCELLED')}
          className={`inline-flex items-center gap-1.5 border-b-2 pb-2 text-xs transition-colors ${
            selectedTab === 'CANCELLED'
              ? 'border-primary font-bold text-primary'
              : 'border-transparent font-medium text-sub hover:text-ink'
          }`}
        >
          <XCircle className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Đã hủy ({tabCounts.cancelled})</span>
        </button>

        <button
          type="button"
          role="tab"
          aria-selected={selectedTab === 'ALL'}
          onClick={() => onSelectTab('ALL')}
          className={`inline-flex items-center gap-1.5 border-b-2 pb-2 text-xs transition-colors ${
            selectedTab === 'ALL'
              ? 'border-primary font-bold text-primary'
              : 'border-transparent font-medium text-sub hover:text-ink'
          }`}
        >
          <span>Tất cả ({tabCounts.all})</span>
        </button>
      </div>
    </section>
  );
}
