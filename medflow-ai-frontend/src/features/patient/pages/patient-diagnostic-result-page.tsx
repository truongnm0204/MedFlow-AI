import { ArrowLeft, FileDown, Lock, Printer } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { Alert } from '@/shared/ui/alert';
import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';

import { PatientDiagnosticPdfPreview } from '../components/patient-diagnostic-pdf-preview';
import { PATIENT_DIAGNOSTIC_RESULT_MOCK } from '../data/patient-diagnostic-result-mock';

export function PatientDiagnosticResultPage() {
  const params = useParams<{ orderCode?: string }>();
  const navigate = useNavigate();

  const sheet = PATIENT_DIAGNOSTIC_RESULT_MOCK;
  const activeOrderCode = params.orderCode?.trim() || sheet.orderCode;
  const [notice, setNotice] = useState<string | null>(null);

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      {/* Dark Viewer Toolbar + Canvas matching Figma PDF Preview */}
      <main className="flex-1 bg-slate-900 py-6 px-4 sm:px-6 lg:px-8">
        <div className="mx-auto max-w-5xl space-y-5">
          {/* Toolbar */}
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-slate-700 bg-slate-800 px-4 py-3 text-white shadow-lg print:hidden">
            <div className="flex items-center gap-3 min-w-0">
              <button
                type="button"
                onClick={() => navigate('/patient/home')}
                aria-label="Quay lại Hồ sơ Bệnh án"
                className="inline-flex items-center gap-1.5 rounded-lg border border-slate-600 bg-slate-700 px-3 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-slate-600"
              >
                <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Quay lại</span>
              </button>
              <div className="min-w-0">
                <h1 className="truncate font-mono text-xs font-bold text-sky-300">
                  {sheet.fileName}
                </h1>
                <p className="truncate text-[11px] text-slate-400">
                  Phiếu Kết quả Cận lâm sàng được công bố cho Người bệnh • {activeOrderCode}
                </p>
              </div>
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <button
                type="button"
                onClick={() => setNotice(`Đang tải xuống tài liệu PDF: ${sheet.fileName}`)}
                className="inline-flex items-center gap-1.5 rounded-lg border border-slate-600 bg-slate-700 px-3 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-slate-600"
              >
                <FileDown className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Tải file PDF</span>
              </button>

              <button
                type="button"
                onClick={() => window.print()}
                className="inline-flex items-center gap-1.5 rounded-lg bg-primary px-3.5 py-1.5 text-xs font-bold text-white shadow-xs transition-colors hover:bg-primary-dark"
              >
                <Printer className="h-3.5 w-3.5" aria-hidden="true" />
                <span>In phiếu kết quả (Ctrl+P)</span>
              </button>
            </div>
          </div>

          {notice && (
            <div role="status" aria-live="polite">
              <Alert tone="info">{notice}</Alert>
            </div>
          )}

          {!sheet.isPublishedToPatient ? (
            <div className="mx-auto max-w-xl rounded-2xl border border-amber-300 bg-amber-50 p-8 text-center text-sm text-amber-900 space-y-2">
              <Lock className="mx-auto h-8 w-8 text-amber-600" aria-hidden="true" />
              <p className="font-bold">Kết quả chưa được công bố cho Bệnh nhân</p>
              <p className="text-xs">
                Bác sĩ chuyên khoa đang thẩm định và ký duyệt kết quả này. Vui lòng quay lại sau.
              </p>
            </div>
          ) : (
            <PatientDiagnosticPdfPreview sheet={{ ...sheet, orderCode: activeOrderCode }} />
          )}
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
