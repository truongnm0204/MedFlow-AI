import { AlertCircle, ArrowLeft, FileDown, FileText, Lock, Printer, RefreshCw } from 'lucide-react';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';

import { PatientDiagnosticPdfPreview } from '../components/patient-diagnostic-pdf-preview';
import {
  usePatientDiagnosticResultDetail,
  usePatientDiagnosticResultsList,
} from '../hooks/use-patient-diagnostic-result';

export function PatientDiagnosticResultPage() {
  const params = useParams<{ orderCode?: string }>();
  const navigate = useNavigate();
  const orderParam = params.orderCode?.trim();

  const detailQuery = usePatientDiagnosticResultDetail(orderParam);
  const listQuery = usePatientDiagnosticResultsList(!orderParam);

  const sheet = detailQuery.data ?? null;

  const handleOpenReportFile = () => {
    if (sheet?.reportFileUrl) {
      window.open(sheet.reportFileUrl, '_blank', 'noopener,noreferrer');
    }
  };

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 bg-slate-900 px-4 py-6 sm:px-6 lg:px-8">
        <div className="mx-auto max-w-5xl space-y-5">
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-slate-700 bg-slate-800 px-4 py-3 text-white shadow-lg print:hidden">
            <div className="flex min-w-0 items-center gap-3">
              <button
                type="button"
                onClick={() => navigate('/patient/visits')}
                aria-label="Quay lại Lịch sử lượt khám"
                className="inline-flex items-center gap-1.5 rounded-lg border border-slate-600 bg-slate-700 px-3 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-slate-600"
              >
                <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Quay lại</span>
              </button>
              <div className="min-w-0">
                <h1 className="truncate font-mono text-xs font-bold text-sky-300">
                  {sheet ? sheet.fileName : 'Kết quả Cận lâm sàng'}
                </h1>
                <p className="truncate text-[11px] text-slate-400">
                  {orderParam
                    ? `Phiếu Kết quả Cận lâm sàng • ${orderParam}`
                    : 'Danh sách kết quả cận lâm sàng đã ghi nhận'}
                </p>
              </div>
            </div>

            {sheet?.isPublishedToPatient && (
              <div className="flex flex-wrap items-center gap-2">
                {sheet.reportFileUrl && (
                  <button
                    type="button"
                    onClick={handleOpenReportFile}
                    className="inline-flex items-center gap-1.5 rounded-lg border border-slate-600 bg-slate-700 px-3 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-slate-600"
                  >
                    <FileDown className="h-3.5 w-3.5" aria-hidden="true" />
                    <span>Tải file PDF</span>
                  </button>
                )}

                <button
                  type="button"
                  onClick={() => window.print()}
                  className="inline-flex items-center gap-1.5 rounded-lg bg-primary px-3.5 py-1.5 text-xs font-bold text-white shadow-xs transition-colors hover:bg-primary-dark"
                >
                  <Printer className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>In phiếu kết quả (Ctrl+P)</span>
                </button>
              </div>
            )}
          </div>

          {!orderParam && (
            <div className="rounded-2xl border border-border bg-white p-6 text-ink shadow-lg">
              <h2 className="text-base font-bold text-navy">Danh sách Kết quả Cận lâm sàng</h2>
              {listQuery.isPending && (
                <p className="mt-4 text-xs text-sub" role="status">
                  Đang tải danh sách kết quả cận lâm sàng...
                </p>
              )}
              {listQuery.isError && (
                <div className="mt-4 flex items-center justify-between gap-3 rounded-xl border border-rose-200 bg-rose-50 p-4 text-xs text-rose-700">
                  <span>Không thể tải danh sách kết quả cận lâm sàng.</span>
                  <button
                    type="button"
                    onClick={() => void listQuery.refetch()}
                    className="inline-flex items-center gap-1 font-semibold underline"
                  >
                    <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                    <span>Thử lại</span>
                  </button>
                </div>
              )}
              {!listQuery.isPending &&
                !listQuery.isError &&
                (listQuery.data?.length ?? 0) === 0 && (
                  <p className="mt-4 text-xs text-sub">
                    Chưa có kết quả cận lâm sàng nào được ghi nhận cho hồ sơ của bạn.
                  </p>
                )}
              {!listQuery.isPending && (listQuery.data?.length ?? 0) > 0 && (
                <div className="mt-4 divide-y divide-slate-100">
                  {listQuery.data?.map((item) => (
                    <div
                      key={item.orderId}
                      className="flex flex-wrap items-center justify-between gap-3 py-3 text-xs"
                    >
                      <div>
                        <p className="font-bold text-navy">
                          {item.serviceName}{' '}
                          <span className="font-mono text-primary">({item.orderCode})</span>
                        </p>
                        <p className="text-sub">
                          {item.patientName} • Lượt khám {item.visitCode} • {item.signedAt}
                        </p>
                      </div>
                      <Link
                        to={`/patient/diagnostic-results/${encodeURIComponent(item.orderCode)}`}
                        className="inline-flex items-center gap-1.5 rounded-lg bg-primary px-3 py-1.5 font-semibold text-white hover:bg-primary-dark"
                      >
                        <FileText className="h-3.5 w-3.5" aria-hidden="true" />
                        <span>Xem phiếu</span>
                      </Link>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}

          {orderParam && detailQuery.isPending && (
            <div
              role="status"
              className="mx-auto max-w-xl rounded-2xl border border-slate-700 bg-slate-800 p-8 text-center text-white"
            >
              <div className="mx-auto h-9 w-9 animate-spin rounded-full border-4 border-slate-600 border-t-sky-400" />
              <p className="mt-3 text-xs font-semibold">Đang tải phiếu kết quả cận lâm sàng...</p>
            </div>
          )}

          {orderParam && !detailQuery.isPending && (detailQuery.isError || !sheet) && (
            <div
              role="alert"
              className="mx-auto max-w-xl space-y-3 rounded-2xl border border-rose-300 bg-rose-50 p-8 text-center text-xs text-rose-900"
            >
              <AlertCircle className="mx-auto h-8 w-8 text-rose-600" aria-hidden="true" />
              <p className="text-sm font-bold">Không tìm thấy hoặc không có quyền xem kết quả</p>
              <p>
                Chỉ định <span className="font-mono font-semibold">{orderParam}</span> chưa có kết
                quả hoặc không thuộc phạm vi hồ sơ bệnh nhân của bạn.
              </p>
              <button
                type="button"
                onClick={() => void detailQuery.refetch()}
                className="inline-flex items-center gap-1.5 rounded-lg bg-rose-600 px-4 py-2 font-semibold text-white hover:bg-rose-700"
              >
                <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Thử lại</span>
              </button>
            </div>
          )}

          {orderParam && !detailQuery.isPending && sheet && !sheet.isPublishedToPatient && (
            <div className="mx-auto max-w-xl space-y-2 rounded-2xl border border-amber-300 bg-amber-50 p-8 text-center text-sm text-amber-900">
              <Lock className="mx-auto h-8 w-8 text-amber-600" aria-hidden="true" />
              <p className="font-bold">Kết quả chưa được công bố cho Bệnh nhân</p>
              <p className="text-xs">
                Bác sĩ chuyên khoa đang thẩm định và ký duyệt kết quả này. Vui lòng quay lại sau.
              </p>
            </div>
          )}

          {orderParam && !detailQuery.isPending && sheet && sheet.isPublishedToPatient && (
            <PatientDiagnosticPdfPreview sheet={sheet} />
          )}
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
