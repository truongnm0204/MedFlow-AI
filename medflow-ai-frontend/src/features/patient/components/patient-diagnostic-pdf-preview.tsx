import { CheckCircle2, ExternalLink, QrCode, ShieldCheck } from 'lucide-react';

import type { PatientDiagnosticResultDetail } from '../types/patient-diagnostic-result.types';

interface PatientDiagnosticPdfPreviewProps {
  sheet: PatientDiagnosticResultDetail;
}

export function PatientDiagnosticPdfPreview({ sheet }: PatientDiagnosticPdfPreviewProps) {
  const findingLines = (sheet.resultText ?? '')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);

  return (
    <article
      aria-label="Phiếu kết quả cận lâm sàng A4"
      className="mx-auto max-w-4xl overflow-hidden rounded-2xl border border-slate-300 bg-white text-ink shadow-2xl"
    >
      <header className="border-b-2 border-navy bg-canvas px-8 py-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-1">
            <p className="text-xs font-bold uppercase tracking-wider text-primary">
              {sheet.hospitalName}
            </p>
            <h2 className="text-xl font-black tracking-tight text-navy">
              PHIẾU KẾT QUẢ CẬN LÂM SÀNG
            </h2>
            <p className="text-xs text-sub">{sheet.performedRoom}</p>
          </div>

          <div className="flex items-center gap-3 rounded-xl border border-border bg-white px-3 py-2 text-right">
            <div>
              <p className="font-mono text-[11px] font-bold text-navy">{sheet.orderCode}</p>
              <p className="text-[10px] text-sub">Ký số: {sheet.signedAt}</p>
            </div>
            <QrCode className="h-9 w-9 text-navy" aria-hidden="true" />
          </div>
        </div>
      </header>

      <div className="space-y-6 px-8 py-6 text-xs">
        <section
          aria-label="Thông tin hành chính bệnh nhân"
          className="grid grid-cols-1 gap-3 rounded-xl border border-border bg-canvas p-4 sm:grid-cols-3"
        >
          <div>
            <span className="text-sub">Họ tên người bệnh:</span>
            <p className="text-sm font-bold text-navy">{sheet.patientName}</p>
            <p className="font-mono text-[11px] text-sub">
              {sheet.patientCode} • {sheet.gender}
              {sheet.birthYear != null ? ` (${sheet.birthYear})` : ''}
            </p>
          </div>
          <div>
            <span className="text-sub">Mã lượt khám &amp; Dịch vụ:</span>
            <p className="font-mono font-bold text-primary">{sheet.visitCode}</p>
            <p className="font-medium text-ink">{sheet.serviceName}</p>
          </div>
          <div>
            <span className="text-sub">Bác sĩ chỉ định &amp; Thực hiện:</span>
            <p className="font-semibold text-navy">{sheet.orderingDoctor}</p>
            <p className="text-sub">Người thực hiện: {sheet.performingDoctor}</p>
          </div>
        </section>

        <section aria-label="Mô tả kết quả cận lâm sàng" className="space-y-2.5">
          <h3 className="text-xs font-bold uppercase tracking-wider text-navy">
            1. Mô tả Kết quả Cận lâm sàng
          </h3>
          {findingLines.length > 0 ? (
            <div className="rounded-xl border border-border bg-canvas p-4 leading-relaxed text-ink">
              <ul className="list-disc space-y-1.5 pl-5">
                {findingLines.map((line, index) => (
                  <li key={`${index}-${line.slice(0, 24)}`}>{line}</li>
                ))}
              </ul>
            </div>
          ) : (
            <p className="rounded-xl border border-border bg-canvas p-4 text-sub">
              Chưa có nội dung mô tả chi tiết trên hệ thống.
            </p>
          )}
        </section>

        <section aria-label="Kết luận chuyên khoa" className="space-y-2.5">
          <h3 className="text-xs font-bold uppercase tracking-wider text-navy">
            2. Kết luận Chuyên khoa
          </h3>
          <div className="rounded-xl border border-sky-200 bg-sky-50/70 p-3.5 text-sm font-bold text-navy">
            {sheet.conclusion?.trim() || 'Chưa có kết luận đính kèm.'}
          </div>
        </section>

        {sheet.reportFileUrl && (
          <section aria-label="Tài liệu gốc đính kèm" className="space-y-2">
            <h3 className="text-xs font-bold uppercase tracking-wider text-navy">
              3. Tài liệu Báo cáo Đính kèm
            </h3>
            <a
              href={sheet.reportFileUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2 rounded-xl border border-border bg-canvas px-4 py-2.5 text-xs font-semibold text-primary transition-colors hover:bg-sky-50"
            >
              <ExternalLink className="h-4 w-4" aria-hidden="true" />
              <span>Mở tệp báo cáo gốc ({sheet.fileName})</span>
            </a>
          </section>
        )}

        <footer className="flex flex-wrap items-end justify-between gap-4 border-t border-border pt-4">
          <div className="max-w-lg text-[11px] leading-relaxed text-sub">
            Phiếu kết quả cận lâm sàng điện tử được trích xuất trực tiếp từ hệ thống hồ sơ bệnh án
            của {sheet.hospitalName}.
          </div>

          <div className="space-y-1 rounded-xl border border-emerald-200 bg-emerald-50/50 px-5 py-3 text-center">
            <div className="flex items-center justify-center gap-1 font-bold text-emerald-700">
              <ShieldCheck className="h-4 w-4" aria-hidden="true" />
              <span>ĐÃ KÝ DUYỆT CHUYÊN MÔN</span>
            </div>
            <p className="font-bold text-navy">{sheet.performingDoctor}</p>
            <p className="flex items-center justify-center gap-1 text-[10px] text-sub">
              <CheckCircle2 className="h-3 w-3 text-emerald-600" aria-hidden="true" />
              <span>Ký duyệt lúc: {sheet.signedAt}</span>
            </p>
          </div>
        </footer>
      </div>
    </article>
  );
}
