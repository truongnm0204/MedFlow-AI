import { ArrowLeft, CheckCircle2, FileDown, Printer, ShieldAlert } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';

import { DiagnosticAnatomicalReport } from '../components/diagnostic-anatomical-report';
import { DiagnosticImagingViewer } from '../components/diagnostic-imaging-viewer';
import { DiagnosticSidebar } from '../components/diagnostic-sidebar';
import { DIAGNOSTIC_RESULT_VIEW_MOCK } from '../data/diagnostic-result-view-mock';

export function DiagnosticResultViewPage() {
  const params = useParams<{ orderIdOrCode?: string }>();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const detail = DIAGNOSTIC_RESULT_VIEW_MOCK;
  const activeOrderCode = params.orderIdOrCode?.trim() || detail.orderCode;
  const [notice, setNotice] = useState<string | null>(null);

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DiagnosticSidebar
        activeItem="results"
        staffName={user?.fullName ?? detail.performingDoctor}
        departmentName={detail.roomName}
        onItemSelect={(item) => {
          if (item === 'dashboard') navigate('/diagnostic');
          if (item === 'orders') navigate('/diagnostic/orders');
        }}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        {/* Top bar */}
        <header className="flex h-16 items-center justify-between gap-4 border-b border-border bg-white px-6">
          <div className="flex min-w-0 items-center gap-3">
            <button
              type="button"
              onClick={() => navigate('/diagnostic')}
              aria-label="Quay lại bàn làm việc cận lâm sàng"
              className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border bg-white text-navy transition-colors hover:bg-canvas"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            </button>
            <div className="min-w-0">
              <h1 className="truncate text-sm font-bold text-navy">
                Chi tiết Phiếu Kết quả Cận lâm sàng (SCR-DIA-08)
              </h1>
              <p className="truncate text-[11px] text-sub">
                Diagnostic Management • Mã chỉ định:{' '}
                <span className="font-mono font-bold text-primary">{activeOrderCode}</span>
              </p>
            </div>
          </div>

          <button
            type="button"
            onClick={() => logout.mutate()}
            disabled={logout.isPending}
            className="rounded-lg border border-border px-3 py-1.5 text-xs font-medium text-ink transition-colors hover:bg-canvas disabled:opacity-50"
          >
            Đăng xuất
          </button>
        </header>

        <main className="flex-1 space-y-5 overflow-y-auto p-6 pb-28">
          {/* Patient & Order Header Banner */}
          <section
            aria-label="Thông tin bệnh nhân và chỉ định CLS"
            className="rounded-2xl border border-border bg-white p-5 shadow-2xs space-y-3"
          >
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div className="flex items-center gap-3.5">
                <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-sky-100 font-bold text-sm text-sky-800 border border-sky-200">
                  NA
                </div>
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <p className="text-base font-bold text-navy">{detail.patientName}</p>
                    <span className="text-xs text-sub">
                      • {detail.gender}, {detail.ageLabel}
                    </span>
                    <Badge tone="emerald">
                      <CheckCircle2 className="mr-1 h-3 w-3" aria-hidden="true" />
                      Đã ký số duyệt KQ
                    </Badge>
                  </div>
                  <p className="mt-0.5 font-mono text-xs text-sub">
                    Mã BN: <strong className="text-navy">{detail.patientCode}</strong> • Lượt khám:{' '}
                    <strong className="text-navy">{detail.visitCode}</strong> • Mã chỉ định:{' '}
                    <strong className="text-primary">{activeOrderCode}</strong>
                  </p>
                </div>
              </div>

              <div className="text-right text-xs">
                <p className="font-bold text-navy">{detail.serviceName}</p>
                <p className="text-sub">
                  BS chỉ định: {detail.orderingDoctor} • Thiết bị: {detail.equipmentName}
                </p>
                <p className="mt-0.5 font-mono text-[11px] text-sub">
                  Thời gian thực hiện: {detail.performedAt} •{' '}
                  <strong className="text-emerald-700">Giá thu: {detail.priceFormatted}</strong>
                </p>
              </div>
            </div>

            <div
              role="note"
              className="flex items-center gap-2 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-2 text-xs font-bold text-rose-700"
            >
              <ShieldAlert className="h-4 w-4 shrink-0" aria-hidden="true" />
              <span>{detail.allergyWarning}</span>
            </div>
          </section>

          {notice && (
            <div role="status" aria-live="polite">
              <Alert tone="info">{notice}</Alert>
            </div>
          )}

          {/* 2-Column Viewer Layout */}
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-12">
            <div className="xl:col-span-5">
              <DiagnosticImagingViewer
                detail={detail}
                onDownloadPacs={() =>
                  setNotice(
                    `Đang tải gói dữ liệu PACS & video (${detail.videoAttachment.fileName})...`,
                  )
                }
              />
            </div>

            <div className="xl:col-span-7">
              <DiagnosticAnatomicalReport detail={detail} />
            </div>
          </div>
        </main>

        {/* Sticky Footer */}
        <footer className="sticky bottom-0 z-10 flex flex-wrap items-center justify-between gap-3 border-t border-border bg-white px-6 py-3 print:hidden">
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={() => navigate('/diagnostic')}
            className="gap-1.5"
          >
            <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            <span>Quay lại</span>
          </Button>

          <div className="flex flex-wrap items-center gap-2.5">
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() =>
                setNotice(`Đã xuất bản xem trước khổ A4 cho chỉ định ${activeOrderCode}.`)
              }
              className="gap-1.5"
            >
              <FileDown className="h-4 w-4" aria-hidden="true" />
              <span>Xem trước in A4 (F12)</span>
            </Button>

            <Button
              type="button"
              variant="primary"
              size="sm"
              onClick={() => window.print()}
              className="gap-1.5 shadow-xs"
            >
              <Printer className="h-4 w-4" aria-hidden="true" />
              <span>In phiếu kết quả</span>
            </Button>
          </div>
        </footer>
      </div>
    </div>
  );
}
