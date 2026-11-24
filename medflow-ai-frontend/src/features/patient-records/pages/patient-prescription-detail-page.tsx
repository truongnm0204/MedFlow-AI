import {
  AlertCircle,
  ArrowLeft,
  Building2,
  Calendar,
  ChevronRight,
  FileText,
  Home,
  Printer,
  RefreshCw,
  Stethoscope,
} from 'lucide-react';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';

import { PatientRecordsSubnav } from '../components/patient-records-subnav';
import { PrescriptionMedicineTable } from '../components/prescription-medicine-table';
import { usePatientPrescriptionDetail } from '../hooks/use-patient-prescriptions';
import { formatEmrDateTimeVn, getPrescriptionStatusMeta } from '../utils/emr-format';

export function PatientPrescriptionDetailPage() {
  const { prescriptionCode } = useParams<{ prescriptionCode: string }>();
  const navigate = useNavigate();
  const query = usePatientPrescriptionDetail(prescriptionCode);
  const detail = query.data;
  const rx = detail?.prescription;
  const statusMeta = getPrescriptionStatusMeta(rx?.status);

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
            <Link to="/patient/prescriptions" className="transition-colors hover:text-primary">
              Đơn thuốc của tôi
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
            <span className="font-mono font-semibold text-ink">
              {rx?.prescriptionCode ?? prescriptionCode}
            </span>
          </nav>

          <PatientRecordsSubnav />

          <div className="mb-5 flex flex-wrap items-center justify-between gap-3 print:hidden">
            <button
              type="button"
              onClick={() => navigate('/patient/prescriptions')}
              className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-3.5 py-2 text-xs font-semibold text-navy transition-colors hover:bg-canvas focus-visible:ring-2 focus-visible:ring-primary"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
              <span>Quay lại danh sách đơn thuốc</span>
            </button>

            {detail && (
              <div className="flex flex-wrap items-center gap-2">
                {rx?.visitCode && (
                  <Link
                    to={`/emr/${encodeURIComponent(rx.visitCode)}`}
                    className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-3.5 py-2 text-xs font-semibold text-navy transition-colors hover:bg-canvas"
                  >
                    <FileText className="h-4 w-4" aria-hidden="true" />
                    <span>Xem bệnh án ({rx.visitCode})</span>
                  </Link>
                )}
                <button
                  type="button"
                  onClick={() => window.print()}
                  className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-4 py-2 text-xs font-semibold text-white shadow-xs transition-colors hover:bg-primary-dark focus-visible:ring-2 focus-visible:ring-primary"
                >
                  <Printer className="h-4 w-4" aria-hidden="true" />
                  <span>In đơn thuốc</span>
                </button>
              </div>
            )}
          </div>

          {query.isPending && (
            <div
              role="status"
              className="rounded-2xl border border-border bg-white p-10 text-center shadow-xs"
            >
              <div className="mx-auto h-9 w-9 animate-spin rounded-full border-4 border-slate-200 border-t-primary" />
              <p className="mt-3 text-sm font-semibold text-ink">Đang tải chi tiết đơn thuốc...</p>
            </div>
          )}

          {!query.isPending && (query.isError || !detail || !rx) && (
            <div
              role="alert"
              className="rounded-2xl border border-rose-200 bg-white p-8 text-center shadow-xs"
            >
              <AlertCircle className="mx-auto h-8 w-8 text-danger" aria-hidden="true" />
              <h1 className="mt-3 text-base font-bold text-ink">
                Không tìm thấy đơn thuốc hoặc không có quyền truy cập
              </h1>
              <p className="mx-auto mt-1 max-w-md text-xs leading-relaxed text-sub">
                {getErrorMessage(
                  query.error,
                  'Đơn thuốc không tồn tại, đang ở trạng thái nháp hoặc không thuộc hồ sơ bệnh nhân của bạn.',
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
                  to="/patient/prescriptions"
                  className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-4 py-2 text-xs font-semibold text-navy hover:bg-canvas"
                >
                  <span>Về danh sách đơn thuốc</span>
                </Link>
              </div>
            </div>
          )}

          {!query.isPending && detail && rx && (
            <div className="space-y-6">
              <section className="rounded-2xl border border-border bg-white p-6 shadow-xs">
                <div className="flex flex-wrap items-start justify-between gap-4 border-b border-border pb-4">
                  <div>
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-mono text-sm font-bold text-primary">
                        {rx.prescriptionCode ?? `RX-${rx.id}`}
                      </span>
                      <span
                        className={`rounded-full border px-2.5 py-0.5 text-[11px] font-semibold ${statusMeta.className}`}
                      >
                        {statusMeta.label}
                      </span>
                    </div>
                    <h1 className="mt-2 text-xl font-bold text-navy">
                      Chi tiết đơn thuốc & Hướng dẫn sử dụng
                    </h1>
                  </div>

                  <div className="text-right text-xs text-sub">
                    <p className="inline-flex items-center gap-1.5 font-semibold text-navy">
                      <Calendar className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                      <span>Ngày kê: {formatEmrDateTimeVn(rx.issuedAt)}</span>
                    </p>
                    <p className="mt-1">Tổng cộng: {detail.items.length} khoản thuốc</p>
                  </div>
                </div>

                <div className="mt-4 grid grid-cols-1 gap-4 text-xs sm:grid-cols-3">
                  <div className="rounded-xl bg-canvas p-3.5">
                    <p className="flex items-center gap-1.5 font-semibold text-sub">
                      <Stethoscope className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                      <span>Bác sĩ kê đơn</span>
                    </p>
                    <p className="mt-1 font-bold text-ink">
                      {rx.prescribingDoctorName ?? 'Bác sĩ điều trị'}
                    </p>
                  </div>

                  <div className="rounded-xl bg-canvas p-3.5">
                    <p className="flex items-center gap-1.5 font-semibold text-sub">
                      <Building2 className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                      <span>Cơ sở khám chữa bệnh</span>
                    </p>
                    <p className="mt-1 font-bold text-ink">
                      {rx.hospitalName ?? 'Hệ thống Y tế MedFlow'}
                    </p>
                  </div>

                  <div className="rounded-xl bg-canvas p-3.5">
                    <p className="flex items-center gap-1.5 font-semibold text-sub">
                      <FileText className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                      <span>Mã lượt khám liên kết</span>
                    </p>
                    <p className="mt-1 font-mono font-bold text-ink">
                      {rx.visitCode ?? `VIS-${rx.visitId}`}
                    </p>
                  </div>
                </div>
              </section>

              <PrescriptionMedicineTable items={detail.items} />
            </div>
          )}
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
