import { useState, type CSSProperties } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import { ArrowRight } from 'lucide-react';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Spinner } from '@/shared/ui/spinner';

import { AppointmentPrintSlip } from '../components/appointment-print-slip';
import { PrintPreviewToolbar, type PrintOrientation } from '../components/print-preview-toolbar';
import { CANCELLED_STATUSES } from '../data/appointment-list-mock';
import { useAppointmentDetailQuery } from '../hooks/use-appointment-detail';

export function AppointmentPrintPreviewPage() {
  const { appointmentId = '' } = useParams<{ appointmentId: string }>();
  const navigate = useNavigate();
  const location = useLocation();

  const [orientation, setOrientation] = useState<PrintOrientation>('portrait');
  const [zoom, setZoom] = useState<number>(100);
  const [printedAtLabel] = useState(() =>
    new Intl.DateTimeFormat('vi-VN', { dateStyle: 'short', timeStyle: 'short' }).format(new Date()),
  );

  const { data: appointment, isLoading, isError, error } = useAppointmentDetailQuery(appointmentId);
  const isCancelled = appointment ? CANCELLED_STATUSES.includes(appointment.status) : false;

  const handleBack = () => {
    if (location.key !== 'default') {
      navigate(-1);
    } else {
      navigate(`/appointments/${appointmentId}`);
    }
  };

  const handlePrint = () => {
    window.print();
  };

  return (
    <div className="min-h-screen bg-slate-100 font-sans text-ink print:bg-white">
      <style>{`@page { size: A5 ${orientation}; margin: 8mm; }`}</style>

      <PrintPreviewToolbar
        orientation={orientation}
        zoom={zoom}
        onBack={handleBack}
        onOrientationChange={setOrientation}
        onZoomChange={setZoom}
        onPrint={handlePrint}
      />

      <main className="mx-auto max-w-6xl px-4 py-10 print:p-0">
        {isLoading ? (
          <div className="flex items-center justify-center rounded-2xl border border-border bg-white p-12 shadow-xs">
            <Spinner className="h-6 w-6" />
          </div>
        ) : isError || !appointment ? (
          <div className="mx-auto max-w-xl space-y-4">
            <Alert tone="error">{getErrorMessage(error)}</Alert>
            <Link
              to="/appointments"
              className="inline-flex items-center justify-center gap-2 rounded-xl bg-primary px-6 py-3 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-primary-dark"
            >
              <span>Về danh sách Lịch hẹn của tôi</span>
              <ArrowRight className="h-4 w-4" aria-hidden="true" />
            </Link>
          </div>
        ) : isCancelled ? (
          <div className="mx-auto max-w-xl">
            <Alert tone="error">Lịch hẹn đã hủy, không thể in phiếu hẹn.</Alert>
          </div>
        ) : (
          <div className="flex justify-center print:p-0">
            <div
              style={{ '--slip-scale': zoom / 100 } as CSSProperties}
              className={`origin-top scale-(--slip-scale) print:scale-100 ${
                orientation === 'portrait' ? 'w-[148mm]' : 'w-[210mm]'
              }`}
            >
              <AppointmentPrintSlip appointment={appointment} printedAtLabel={printedAtLabel} />
            </div>
          </div>
        )}
      </main>
    </div>
  );
}
