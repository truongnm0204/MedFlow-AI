import {
  AlertCircle,
  ArrowLeft,
  Building2,
  Calendar,
  ChevronRight,
  Home,
  Printer,
  RefreshCw,
  ShieldCheck,
  UserRound,
} from 'lucide-react';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';

import { MedicalRecordClinicalSections } from '../components/medical-record-clinical-sections';
import { MedicalRecordVitalsCard } from '../components/medical-record-vitals-card';
import { PatientRecordsSubnav } from '../components/patient-records-subnav';
import { usePatientMedicalRecordDetail } from '../hooks/use-patient-medical-records';
import { formatEmrDateTimeVn, formatEmrDateVn, getGenderLabelVi } from '../utils/emr-format';

export function MedicalRecordDetailPage() {
  const { recordId } = useParams<{ recordId: string }>();
  const navigate = useNavigate();
  const query = usePatientMedicalRecordDetail(recordId);
  const detail = query.data;

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 pt-5 pb-16">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <nav
            aria-label="Breadcrumb"
            className="mb-4 flex flex-wrap items-center gap-2 text-xs text-sub print:hidden"
          >
            <Link to="/" className="flex items-center gap-1.5 transition-colors hover:text-primary">
              <Home className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Trang chủ</span>
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
            <Link to="/emr" className="transition-colors hover:text-primary">
              Hồ sơ bệnh án EMR
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
            <span className="font-mono font-semibold text-ink">
              {detail?.recordCode ?? recordId}
            </span>
          </nav>

          <PatientRecordsSubnav />

          <div className="mb-5 flex flex-wrap items-center justify-between gap-3 print:hidden">
            <button
              type="button"
              onClick={() => navigate('/emr')}
              className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-3.5 py-2 text-xs font-semibold text-navy transition-colors hover:bg-canvas focus-visible:ring-2 focus-visible:ring-primary"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
              <span>Quay lại danh sách bệnh án</span>
            </button>

            {detail && (
              <button
                type="button"
                onClick={() => window.print()}
                className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-4 py-2 text-xs font-semibold text-white shadow-xs transition-colors hover:bg-primary-dark focus-visible:ring-2 focus-visible:ring-primary"
              >
                <Printer className="h-4 w-4" aria-hidden="true" />
                <span>In tóm tắt bệnh án</span>
              </button>
            )}
          </div>

          {query.isPending && (
            <div
              role="status"
              className="rounded-2xl border border-border bg-white p-10 text-center shadow-xs"
            >
              <div className="mx-auto h-9 w-9 animate-spin rounded-full border-4 border-slate-200 border-t-primary" />
              <p className="mt-3 text-sm font-semibold text-ink">
                Đang tải chi tiết hồ sơ bệnh án...
              </p>
            </div>
          )}

          {!query.isPending && (query.isError || !detail) && (
            <div
              role="alert"
              className="rounded-2xl border border-rose-200 bg-white p-8 text-center shadow-xs"
            >
              <AlertCircle className="mx-auto h-8 w-8 text-danger" aria-hidden="true" />
              <h1 className="mt-3 text-base font-bold text-ink">
                Không tìm thấy hoặc không có quyền truy cập bệnh án
              </h1>
              <p className="mx-auto mt-1 max-w-md text-xs leading-relaxed text-sub">
                {getErrorMessage(
                  query.error,
                  'Bệnh án chưa được bác sĩ khóa hoàn tất hoặc nằm ngoài phạm vi hồ sơ đã xác minh PHI của bạn.',
                )}
              </p>
              <div className="mt-5 flex flex-wrap items-center justify-center gap-3">
                <button
                  type="button"
                  onClick={() => void query.refetch()}
                  className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-4 py-2 text-xs font-semibold text-white hover:bg-primary-dark"
                >
                  <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Thử lại</span>
                </button>
                <Link
                  to="/emr"
                  className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-4 py-2 text-xs font-semibold text-navy hover:bg-canvas"
                >
                  <span>Về danh sách EMR</span>
                </Link>
              </div>
            </div>
          )}

          {!query.isPending && detail && (
            <div className="space-y-6">
              <section className="rounded-2xl border border-border bg-white p-6 shadow-xs">
                <div className="flex flex-wrap items-start justify-between gap-4 border-b border-border pb-4">
                  <div>
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-mono text-sm font-bold text-primary">
                        {detail.recordCode}
                      </span>
                      {detail.visitCode && (
                        <span className="rounded-md border border-border bg-canvas px-2.5 py-0.5 font-mono text-xs font-semibold text-sub">
                          Lượt khám: {detail.visitCode}
                        </span>
                      )}
                      <span className="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-2.5 py-0.5 text-[11px] font-semibold text-emerald-700">
                        <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
                        <span>Bệnh án đã khóa xác thực</span>
                      </span>
                    </div>
                    <h1 className="mt-2 text-xl font-bold text-navy">
                      Hồ sơ khám bệnh ngoại trú — {detail.patientName}
                    </h1>
                  </div>

                  <div className="text-right text-xs text-sub">
                    <p className="inline-flex items-center gap-1.5 font-semibold text-navy">
                      <Calendar className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                      <span>Ngày khám: {formatEmrDateVn(detail.visitDate)}</span>
                    </p>
                    {detail.finalizedAt && (
                      <p className="mt-1">Khóa hồ sơ: {formatEmrDateTimeVn(detail.finalizedAt)}</p>
                    )}
                  </div>
                </div>

                <div className="mt-4 grid grid-cols-1 gap-4 text-xs sm:grid-cols-2 lg:grid-cols-4">
                  <div className="rounded-xl bg-canvas p-3.5">
                    <p className="flex items-center gap-1.5 font-semibold text-sub">
                      <UserRound className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                      <span>Thông tin bệnh nhân</span>
                    </p>
                    <p className="mt-1 font-bold text-ink">
                      {detail.patientName} ({detail.relationshipLabel})
                    </p>
                    <p className="mt-0.5 text-sub">
                      {detail.patientCode && (
                        <span className="font-mono">{detail.patientCode} • </span>
                      )}
                      {getGenderLabelVi(detail.gender)}
                      {detail.dateOfBirth ? ` • NS: ${formatEmrDateVn(detail.dateOfBirth)}` : ''}
                      {detail.bloodType ? ` • Nhóm máu ${detail.bloodType}` : ''}
                    </p>
                  </div>

                  <div className="rounded-xl bg-canvas p-3.5">
                    <p className="flex items-center gap-1.5 font-semibold text-sub">
                      <Building2 className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                      <span>Cơ sở & Khoa phòng</span>
                    </p>
                    <p className="mt-1 font-bold text-ink">
                      {detail.hospitalName ?? 'Hệ thống Y tế MedFlow'}
                    </p>
                    <p className="mt-0.5 text-sub">
                      {[detail.departmentName, detail.roomName].filter(Boolean).join(' • ') ||
                        'Khoa Khám bệnh'}
                    </p>
                  </div>

                  <div className="rounded-xl bg-canvas p-3.5">
                    <p className="font-semibold text-sub">Chuyên khoa & Bác sĩ</p>
                    <p className="mt-1 font-bold text-ink">
                      {detail.doctorName ?? 'Bác sĩ chuyên khoa'}
                    </p>
                    <p className="mt-0.5 text-sub">{detail.specialtyName ?? 'Đa khoa'}</p>
                  </div>

                  <div className="rounded-xl bg-canvas p-3.5">
                    <p className="font-semibold text-sub">Thời gian tiếp nhận & hoàn tất</p>
                    <p className="mt-1 font-medium text-ink">
                      Tiếp nhận: {formatEmrDateTimeVn(detail.checkedInAt)}
                    </p>
                    <p className="mt-0.5 text-sub">
                      Hoàn tất: {formatEmrDateTimeVn(detail.completedAt)}
                    </p>
                  </div>
                </div>
              </section>

              <MedicalRecordVitalsCard vitalSigns={detail.vitalSigns} />

              <MedicalRecordClinicalSections detail={detail} />
            </div>
          )}
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
