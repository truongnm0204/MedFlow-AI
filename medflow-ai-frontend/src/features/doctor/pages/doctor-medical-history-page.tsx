import axios from 'axios';
import {
  Activity,
  ArrowLeft,
  ClipboardList,
  Filter,
  History,
  RefreshCw,
  RotateCcw,
  Search,
  ShieldAlert,
} from 'lucide-react';
import { type FormEvent, useMemo, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useRoleAccess } from '@/features/auth/hooks/use-role-access';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';
import { Spinner } from '@/shared/ui/spinner';

import { DoctorSidebar } from '../components/doctor-sidebar';
import { MedicalHistoryPatientBanner } from '../components/medical-history-patient-banner';
import { MedicalHistoryStatCards } from '../components/medical-history-stat-cards';
import { MedicalHistoryVisitCard } from '../components/medical-history-visit-card';
import { DOCTOR_WORKBENCH_META } from '../data/doctor-mock-data';
import { DEFAULT_MEDICAL_HISTORY_PATIENT_CODE } from '../data/medical-history-mock';
import { useMedicalHistory } from '../hooks/use-medical-history';
import type {
  GetPatientMedicalHistoryQueryParams,
  MedicalHistoryFilterStatus,
  MedicalHistoryLookupMode,
} from '../types/medical-history.types';
import {
  MEDICAL_HISTORY_LIMIT_OPTIONS,
  MEDICAL_HISTORY_STATUS_FILTER_OPTIONS,
  validateMedicalHistoryFilterForm,
} from '../utils/medical-history-meta';

export function DoctorMedicalHistoryPage() {
  const params = useParams<{ patientIdOrCode?: string; visitIdOrCode?: string }>();
  const [searchParams] = useSearchParams();

  const routeVisitId = params.visitIdOrCode?.trim() || searchParams.get('visitIdOrCode')?.trim();
  const routePatientId =
    params.patientIdOrCode?.trim() || searchParams.get('patientIdOrCode')?.trim();

  const initialMode: MedicalHistoryLookupMode =
    params.visitIdOrCode && !params.patientIdOrCode ? 'visit' : 'patient';
  const initialIdentifier =
    (initialMode === 'visit' ? routeVisitId : routePatientId) ||
    DEFAULT_MEDICAL_HISTORY_PATIENT_CODE;

  return (
    <DoctorMedicalHistoryWorkbench
      key={`${initialMode}:${initialIdentifier}:${routeVisitId ?? ''}`}
      initialMode={initialMode}
      initialIdentifier={initialIdentifier}
      routeVisitId={routeVisitId}
    />
  );
}

interface DoctorMedicalHistoryWorkbenchProps {
  initialMode: MedicalHistoryLookupMode;
  initialIdentifier: string;
  routeVisitId?: string;
}

function DoctorMedicalHistoryWorkbench({
  initialMode,
  initialIdentifier,
  routeVisitId,
}: DoctorMedicalHistoryWorkbenchProps) {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();
  const { hasAnyPermission } = useRoleAccess();

  const canViewMedicalHistory = hasAnyPermission(['MEDICAL_RECORD_WRITE', 'PATIENT_PROFILE_READ']);

  // Draft form state
  const [modeInput, setModeInput] = useState<MedicalHistoryLookupMode>(initialMode);
  const [identifierInput, setIdentifierInput] = useState<string>(initialIdentifier);
  const [visitContextInput, setVisitContextInput] = useState<string>(
    initialMode === 'patient' && routeVisitId ? routeVisitId : '',
  );
  const [statusInput, setStatusInput] = useState<MedicalHistoryFilterStatus>('ALL');
  const [specialtyCodeInput, setSpecialtyCodeInput] = useState<string>('');
  const [fromDateInput, setFromDateInput] = useState<string>('');
  const [toDateInput, setToDateInput] = useState<string>('');
  const [keywordInput, setKeywordInput] = useState<string>('');
  const [includeCurrentVisitInput, setIncludeCurrentVisitInput] = useState<boolean>(true);
  const [limitInput, setLimitInput] = useState<number>(20);
  const [validationError, setValidationError] = useState<string | null>(null);

  // Committed query state
  const [activeMode, setActiveMode] = useState<MedicalHistoryLookupMode>(initialMode);
  const [activeIdentifier, setActiveIdentifier] = useState<string>(initialIdentifier);
  const [activeQueryParams, setActiveQueryParams] = useState<GetPatientMedicalHistoryQueryParams>(
    () => ({
      visitIdOrCode: initialMode === 'patient' && routeVisitId ? routeVisitId : undefined,
      includeCurrentVisit: true,
      limit: 20,
    }),
  );
  const { data, isPending, isError, error, refetch, isFetching } = useMedicalHistory({
    mode: activeMode,
    identifier: activeIdentifier,
    params: activeQueryParams,
    enabled: canViewMedicalHistory,
  });

  const doctorDisplayName =
    data?.currentVisit?.doctorName?.trim() ||
    user?.fullName?.trim() ||
    DOCTOR_WORKBENCH_META.defaultDoctorName;

  const roomLabel = data?.currentVisit?.roomCode
    ? `${data.currentVisit.roomCode} • ${data.currentVisit.departmentName ?? DOCTOR_WORKBENCH_META.departmentName}`
    : `${DOCTOR_WORKBENCH_META.roomCode} • ${DOCTOR_WORKBENCH_META.departmentName}`;

  const errorHttpStatus = axios.isAxiosError(error) ? error.response?.status : undefined;

  const aggregatedDiagnoses = useMemo(() => {
    if (!data) return [];
    const map = new Map<
      string,
      { code: string; name: string; count: number; isPrimary: boolean }
    >();
    for (const visit of data.visits) {
      for (const diag of visit.diagnoses) {
        const existing = map.get(diag.icd10Code);
        if (existing) {
          existing.count += 1;
          existing.isPrimary = existing.isPrimary || diag.isPrimary;
        } else {
          map.set(diag.icd10Code, {
            code: diag.icd10Code,
            name: diag.icd10NameVi,
            count: 1,
            isPrimary: diag.isPrimary,
          });
        }
      }
    }
    return Array.from(map.values());
  }, [data]);

  const recentVitalSignsHistory = useMemo(() => {
    if (!data) return [];
    return data.visits
      .filter((v) => v.medicalRecord?.vitalSigns)
      .slice(0, 5)
      .map((v) => ({
        visitCode: v.visitCode,
        visitDate: v.visitDate,
        vitalSigns: v.medicalRecord!.vitalSigns!,
      }));
  }, [data]);

  const handleApplyFilters = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const validationMsg = validateMedicalHistoryFilterForm({
      identifier: identifierInput,
      mode: modeInput,
      status: statusInput,
      specialtyCode: specialtyCodeInput,
      fromDate: fromDateInput,
      toDate: toDateInput,
      keyword: keywordInput,
      limit: limitInput,
    });

    if (validationMsg) {
      setValidationError(validationMsg);
      return;
    }

    setValidationError(null);
    const cleanedIdentifier = identifierInput.trim().replace(/^#/, '').trim();
    setActiveMode(modeInput);
    setActiveIdentifier(cleanedIdentifier);
    setActiveQueryParams({
      visitIdOrCode:
        modeInput === 'patient' && visitContextInput.trim() ? visitContextInput.trim() : undefined,
      status: statusInput !== 'ALL' ? statusInput : undefined,
      specialtyCode: specialtyCodeInput.trim() || undefined,
      fromDate: fromDateInput.trim() || undefined,
      toDate: toDateInput.trim() || undefined,
      keyword: keywordInput.trim() || undefined,
      includeCurrentVisit: includeCurrentVisitInput,
      limit: limitInput,
    });
  };

  const handleResetFilters = () => {
    setValidationError(null);
    setStatusInput('ALL');
    setSpecialtyCodeInput('');
    setFromDateInput('');
    setToDateInput('');
    setKeywordInput('');
    setIncludeCurrentVisitInput(true);
    setLimitInput(20);
    setActiveQueryParams({
      visitIdOrCode:
        modeInput === 'patient' && visitContextInput.trim() ? visitContextInput.trim() : undefined,
      includeCurrentVisit: true,
      limit: 20,
    });
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DoctorSidebar
        activeItem="consultation"
        doctorName={doctorDisplayName}
        roomLabel={roomLabel}
        onItemSelect={(item) => {
          if (item === 'dashboard') {
            navigate('/doctor');
          } else if (item === 'assigned-queue') {
            navigate('/doctor/assigned-queue');
          } else if (item === 'consultation') {
            navigate('/doctor/consultation');
          }
        }}
      />

      <div className="flex-1 flex flex-col min-w-0">
        <header className="flex h-16 items-center justify-between border-b border-border bg-white px-6 gap-4">
          <div className="flex items-center gap-3 min-w-0">
            <button
              type="button"
              onClick={() => navigate('/doctor/assigned-queue')}
              aria-label="Quay lại hàng đợi được phân công"
              className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border bg-white text-navy transition-colors hover:bg-canvas"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            </button>
            <div className="min-w-0">
              <h1 className="text-sm font-bold text-navy truncate">
                Lịch sử khám bệnh lâm sàng (UC32)
              </h1>
              <p className="text-[11px] text-sub truncate">
                Tra cứu bệnh sử dọc, sinh hiệu, ICD-10, cận lâm sàng và đơn thuốc trong phạm vi phụ
                trách
              </p>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={() => void refetch()}
              disabled={isFetching || !canViewMedicalHistory}
              aria-label="Làm mới lịch sử khám"
              className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-semibold text-navy transition-colors hover:bg-canvas disabled:opacity-60"
            >
              <RefreshCw
                className={`h-3.5 w-3.5 ${isFetching ? 'animate-spin' : ''}`}
                aria-hidden="true"
              />
              <span>Làm mới</span>
            </button>
            <button
              type="button"
              onClick={() => logout.mutate()}
              disabled={logout.isPending}
              className="rounded-lg border border-border px-3 py-1.5 text-xs font-medium text-ink transition-colors hover:bg-canvas disabled:opacity-50"
            >
              Đăng xuất
            </button>
          </div>
        </header>

        <main className="flex-1 p-6 space-y-6 overflow-y-auto">
          {!canViewMedicalHistory ? (
            <div role="alert">
              <Alert tone="error">
                Tài khoản Bác sĩ hiện tại thiếu quyền truy cập lịch sử khám bệnh lâm sàng
                (MEDICAL_RECORD_WRITE hoặc PATIENT_PROFILE_READ).
              </Alert>
            </div>
          ) : (
            <>
              {/* Bộ lọc & Tra cứu lịch sử khám */}
              <Card>
                <CardHeader className="py-3.5">
                  <div className="flex items-center gap-2">
                    <Filter className="h-4 w-4 text-primary" aria-hidden="true" />
                    <span className="text-sm font-bold text-navy">
                      Tra cứu & Bộ lọc lịch sử khám bệnh
                    </span>
                  </div>
                  <span className="text-xs text-sub">
                    Hỗ trợ tra cứu theo mã bệnh nhân hoặc trực tiếp từ mã lượt khám hiện tại
                  </span>
                </CardHeader>
                <CardContent className="p-4">
                  <form onSubmit={handleApplyFilters} className="space-y-4" noValidate>
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3">
                      <label className="flex flex-col gap-1 text-xs font-semibold text-navy">
                        <span>Phương thức tra cứu</span>
                        <select
                          aria-label="Phương thức tra cứu"
                          value={modeInput}
                          onChange={(e) => setModeInput(e.target.value as MedicalHistoryLookupMode)}
                          className="h-9 rounded-lg border border-border bg-white px-2.5 text-xs font-medium text-ink focus:border-primary focus:outline-none"
                        >
                          <option value="patient">Theo mã bệnh nhân</option>
                          <option value="visit">Theo mã lượt khám</option>
                        </select>
                      </label>

                      <label className="flex flex-col gap-1 text-xs font-semibold text-navy">
                        <span>
                          {modeInput === 'visit' ? 'Mã / ID lượt khám' : 'Mã / ID bệnh nhân'}
                        </span>
                        <input
                          type="text"
                          aria-label="Mã bệnh nhân hoặc mã lượt khám"
                          value={identifierInput}
                          onChange={(e) => setIdentifierInput(e.target.value)}
                          placeholder={
                            modeInput === 'visit' ? 'VD: VIS-2026-0212' : 'VD: BN-2026-0212'
                          }
                          className="h-9 rounded-lg border border-border bg-white px-3 text-xs font-mono text-ink focus:border-primary focus:outline-none"
                        />
                      </label>

                      <label className="flex flex-col gap-1 text-xs font-semibold text-navy">
                        <span>Trạng thái lượt khám / bệnh án</span>
                        <select
                          aria-label="Lọc theo trạng thái"
                          value={statusInput}
                          onChange={(e) =>
                            setStatusInput(e.target.value as MedicalHistoryFilterStatus)
                          }
                          className="h-9 rounded-lg border border-border bg-white px-2.5 text-xs font-medium text-ink focus:border-primary focus:outline-none"
                        >
                          {MEDICAL_HISTORY_STATUS_FILTER_OPTIONS.map((opt) => (
                            <option key={opt.id} value={opt.id}>
                              {opt.label}
                            </option>
                          ))}
                        </select>
                      </label>

                      <label className="flex flex-col gap-1 text-xs font-semibold text-navy">
                        <span>Mã chuyên khoa</span>
                        <input
                          type="text"
                          aria-label="Lọc theo mã chuyên khoa"
                          value={specialtyCodeInput}
                          onChange={(e) => setSpecialtyCodeInput(e.target.value)}
                          placeholder="VD: CARDIOLOGY"
                          className="h-9 rounded-lg border border-border bg-white px-3 text-xs font-mono text-ink focus:border-primary focus:outline-none"
                        />
                      </label>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3">
                      <label className="flex flex-col gap-1 text-xs font-semibold text-navy">
                        <span>Từ ngày</span>
                        <input
                          type="date"
                          aria-label="Từ ngày"
                          value={fromDateInput}
                          onChange={(e) => setFromDateInput(e.target.value)}
                          className="h-9 rounded-lg border border-border bg-white px-2.5 text-xs font-mono text-ink focus:border-primary focus:outline-none"
                        />
                      </label>

                      <label className="flex flex-col gap-1 text-xs font-semibold text-navy">
                        <span>Đến ngày</span>
                        <input
                          type="date"
                          aria-label="Đến ngày"
                          value={toDateInput}
                          onChange={(e) => setToDateInput(e.target.value)}
                          className="h-9 rounded-lg border border-border bg-white px-2.5 text-xs font-mono text-ink focus:border-primary focus:outline-none"
                        />
                      </label>

                      <label className="flex flex-col gap-1 text-xs font-semibold text-navy">
                        <span>Từ khóa lâm sàng / ICD-10 / thuốc</span>
                        <div className="relative">
                          <Search
                            className="h-3.5 w-3.5 text-sub absolute left-2.5 top-1/2 -translate-y-1/2"
                            aria-hidden="true"
                          />
                          <input
                            type="search"
                            aria-label="Tìm kiếm trong lịch sử khám"
                            value={keywordInput}
                            onChange={(e) => setKeywordInput(e.target.value)}
                            placeholder="Triệu chứng, mã ICD-10, tên thuốc..."
                            className="h-9 w-full rounded-lg border border-border bg-white pl-8 pr-3 text-xs text-ink focus:border-primary focus:outline-none"
                          />
                        </div>
                      </label>

                      <label className="flex flex-col gap-1 text-xs font-semibold text-navy">
                        <span>Số lượng hiển thị</span>
                        <select
                          aria-label="Số lượng hiển thị"
                          value={limitInput}
                          onChange={(e) => setLimitInput(Number(e.target.value))}
                          className="h-9 rounded-lg border border-border bg-white px-2.5 text-xs font-mono text-ink focus:border-primary focus:outline-none"
                        >
                          {MEDICAL_HISTORY_LIMIT_OPTIONS.map((limitValue) => (
                            <option key={limitValue} value={limitValue}>
                              Tối đa {limitValue} lượt khám
                            </option>
                          ))}
                        </select>
                      </label>
                    </div>

                    <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 pt-1">
                      <div className="flex flex-wrap items-center gap-4">
                        <label className="inline-flex items-center gap-2 text-xs font-medium text-ink cursor-pointer">
                          <input
                            type="checkbox"
                            aria-label="Bao gồm lượt khám hiện tại"
                            checked={includeCurrentVisitInput}
                            onChange={(e) => setIncludeCurrentVisitInput(e.target.checked)}
                            className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
                          />
                          <span>Bao gồm lượt khám hiện tại trong dòng thời gian</span>
                        </label>

                        {modeInput === 'patient' && (
                          <div className="inline-flex items-center gap-2 text-xs">
                            <span className="text-sub">Ngữ cảnh lượt khám (tùy chọn):</span>
                            <input
                              type="text"
                              aria-label="Mã lượt khám ngữ cảnh"
                              value={visitContextInput}
                              onChange={(e) => setVisitContextInput(e.target.value)}
                              placeholder="VD: VIS-2026-0212"
                              className="h-8 rounded-lg border border-border bg-white px-2.5 text-xs font-mono text-ink focus:border-primary focus:outline-none"
                            />
                          </div>
                        )}
                      </div>

                      <div className="flex items-center gap-2">
                        <button
                          type="button"
                          onClick={handleResetFilters}
                          className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-3 py-2 text-xs font-semibold text-navy transition-colors hover:bg-canvas"
                        >
                          <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
                          <span>Đặt lại bộ lọc</span>
                        </button>
                        <button
                          type="submit"
                          className="inline-flex items-center gap-1.5 rounded-lg bg-primary px-4 py-2 text-xs font-semibold text-white transition-colors hover:bg-primary-dark"
                        >
                          <Search className="h-3.5 w-3.5" aria-hidden="true" />
                          <span>Áp dụng bộ lọc</span>
                        </button>
                      </div>
                    </div>
                  </form>

                  {validationError && (
                    <div role="alert" className="mt-3">
                      <Alert tone="error">{validationError}</Alert>
                    </div>
                  )}
                </CardContent>
              </Card>

              {isPending && (
                <Card>
                  <CardContent className="flex items-center justify-center gap-3 py-12 text-xs text-sub">
                    <Spinner className="h-5 w-5 text-primary" />
                    <span>Đang tải hồ sơ lịch sử khám bệnh lâm sàng...</span>
                  </CardContent>
                </Card>
              )}

              {isError && (
                <div role="alert">
                  <Alert tone="error">
                    <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
                      <div className="space-y-1">
                        <div className="font-bold">
                          {getErrorMessage(error, 'Không thể tải lịch sử khám bệnh của bệnh nhân.')}
                        </div>
                        {errorHttpStatus === 403 && (
                          <div className="text-[11px]">
                            Bác sĩ chỉ được phép truy cập lịch sử khám của bệnh nhân thuộc cùng cơ
                            sở y tế và nằm trong phạm vi phụ trách lâm sàng.
                          </div>
                        )}
                        {errorHttpStatus === 404 && (
                          <div className="text-[11px]">
                            Không tìm thấy hồ sơ bệnh nhân hoặc lượt khám tương ứng với mã định danh
                            đã nhập.
                          </div>
                        )}
                        {errorHttpStatus === 409 && (
                          <div className="text-[11px]">
                            Lượt khám đã bị hủy hoặc vắng mặt, vui lòng tra cứu theo mã bệnh nhân
                            hoặc chọn lượt khám hợp lệ khác.
                          </div>
                        )}
                      </div>
                      <button
                        type="button"
                        onClick={() => void refetch()}
                        className="inline-flex items-center justify-center rounded-lg border border-rose-300 bg-white px-3 py-1.5 text-xs font-semibold text-rose-700 hover:bg-rose-100 shrink-0"
                      >
                        Thử lại
                      </button>
                    </div>
                  </Alert>
                </div>
              )}

              {data && (
                <>
                  <MedicalHistoryPatientBanner
                    patient={data.patient}
                    currentVisit={data.currentVisit}
                    facilityName={data.facilityName}
                    generatedAtUtc={data.generatedAtUtc}
                  />

                  <MedicalHistoryStatCards stats={data.stats} />

                  <div className="grid grid-cols-1 xl:grid-cols-12 gap-6 items-start">
                    {/* Cột chính (8 cột): Dòng thời gian các lượt khám */}
                    <div className="xl:col-span-8 space-y-4">
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-2">
                          <History className="h-4 w-4 text-primary" aria-hidden="true" />
                          <h2 className="text-sm font-bold text-navy">
                            Dòng thời gian lịch sử khám ({data.visits.length} lượt khám)
                          </h2>
                        </div>
                        <span className="text-xs text-sub">
                          Sắp xếp từ lượt khám mới nhất đến cũ nhất
                        </span>
                      </div>

                      {data.visits.length === 0 ? (
                        <Card>
                          <CardContent className="py-10 text-center space-y-2">
                            <p className="text-sm font-semibold text-navy">
                              Không có lịch sử khám phù hợp với bộ lọc hiện tại.
                            </p>
                            <p className="text-xs text-sub">
                              Hãy thử điều chỉnh khoảng thời gian, trạng thái hoặc từ khóa tìm kiếm.
                            </p>
                          </CardContent>
                        </Card>
                      ) : (
                        <div className="space-y-4">
                          {data.visits.map((visit, index) => (
                            <MedicalHistoryVisitCard
                              key={visit.visitId}
                              visit={visit}
                              defaultExpanded={index === 0 || visit.isCurrentVisit}
                            />
                          ))}
                        </div>
                      )}
                    </div>

                    {/* Cột phụ (4 cột): Tóm tắt ICD-10 tích lũy & Diễn biến sinh hiệu */}
                    <div className="xl:col-span-4 space-y-4">
                      <Card>
                        <CardHeader className="py-3.5">
                          <div className="flex items-center gap-2">
                            <ClipboardList className="h-4 w-4 text-primary" aria-hidden="true" />
                            <h3 className="text-xs font-bold uppercase tracking-wider text-navy">
                              Tổng hợp mã bệnh ICD-10 đã ghi nhận
                            </h3>
                          </div>
                        </CardHeader>
                        <CardContent className="p-4 space-y-2.5">
                          {aggregatedDiagnoses.length === 0 ? (
                            <p className="text-xs text-sub">
                              Chưa có chẩn đoán ICD-10 nào trong danh sách hiển thị.
                            </p>
                          ) : (
                            aggregatedDiagnoses.map((item) => (
                              <div
                                key={item.code}
                                className="flex items-start justify-between gap-2 rounded-lg border border-border bg-canvas p-2.5 text-xs"
                              >
                                <div className="space-y-0.5 min-w-0">
                                  <div className="flex items-center gap-1.5">
                                    <span className="font-mono font-bold text-primary">
                                      {item.code}
                                    </span>
                                    {item.isPrimary && <Badge tone="emerald">Chính</Badge>}
                                  </div>
                                  <div className="text-ink font-medium">{item.name}</div>
                                </div>
                                <span className="font-mono text-[11px] font-semibold text-sub shrink-0">
                                  {item.count} lần
                                </span>
                              </div>
                            ))
                          )}
                        </CardContent>
                      </Card>

                      <Card>
                        <CardHeader className="py-3.5">
                          <div className="flex items-center gap-2">
                            <Activity className="h-4 w-4 text-primary" aria-hidden="true" />
                            <h3 className="text-xs font-bold uppercase tracking-wider text-navy">
                              Đối chiếu sinh hiệu qua các lần khám
                            </h3>
                          </div>
                        </CardHeader>
                        <CardContent className="p-4 space-y-2.5">
                          {recentVitalSignsHistory.length === 0 ? (
                            <p className="text-xs text-sub">
                              Chưa có dữ liệu sinh hiệu trong các lượt khám hiển thị.
                            </p>
                          ) : (
                            recentVitalSignsHistory.map((entry) => (
                              <div
                                key={entry.visitCode}
                                className="rounded-lg border border-border bg-white p-2.5 text-xs space-y-1"
                              >
                                <div className="flex items-center justify-between font-mono">
                                  <span className="font-bold text-navy">{entry.visitDate}</span>
                                  <span className="text-primary font-semibold">
                                    {entry.visitCode}
                                  </span>
                                </div>
                                <div className="grid grid-cols-3 gap-2 pt-1 text-[11px]">
                                  <div>
                                    <span className="text-sub">HA: </span>
                                    <strong className="font-mono text-navy">
                                      {entry.vitalSigns.bloodPressureLabel || '—'}
                                    </strong>
                                  </div>
                                  <div>
                                    <span className="text-sub">Mạch: </span>
                                    <strong className="font-mono text-navy">
                                      {entry.vitalSigns.pulseBpm ?? '—'}
                                    </strong>
                                  </div>
                                  <div>
                                    <span className="text-sub">BMI: </span>
                                    <strong className="font-mono text-navy">
                                      {entry.vitalSigns.bmi ?? '—'}
                                    </strong>
                                  </div>
                                </div>
                              </div>
                            ))
                          )}
                        </CardContent>
                      </Card>

                      <Card>
                        <CardContent className="p-4 space-y-2 text-xs text-sub">
                          <div className="flex items-center gap-2 font-bold text-navy">
                            <ShieldAlert className="h-4 w-4 text-amber-600" aria-hidden="true" />
                            <span>Quy tắc bảo mật & Phạm vi lâm sàng</span>
                          </div>
                          <p>
                            • Chỉ hiển thị các lượt khám trong cùng cơ sở y tế{' '}
                            <strong className="text-ink">{data.facilityName}</strong>.
                          </p>
                          <p>
                            • Các lượt khám đã hủy (CANCELLED) hoặc vắng mặt (NO_SHOW) được loại trừ
                            khỏi dòng thời gian lâm sàng để tránh nhiễu thông tin điều trị.
                          </p>
                        </CardContent>
                      </Card>
                    </div>
                  </div>
                </>
              )}
            </>
          )}
        </main>
      </div>
    </div>
  );
}
