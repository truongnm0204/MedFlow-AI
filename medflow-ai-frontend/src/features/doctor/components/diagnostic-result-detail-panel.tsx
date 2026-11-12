import { FileDown } from 'lucide-react';

import { Alert } from '@/shared/ui/alert';

import type { ConsultationDiagnosticResultItemDto } from '../types/review-diagnostic.types';
import {
  formatResultDateTime,
  isSafeReportUrl,
  splitResultText,
} from '../utils/review-diagnostic-meta';

export interface DiagnosticResultDetailPanelProps {
  result: ConsultationDiagnosticResultItemDto;
}

export function DiagnosticResultDetailPanel({ result }: DiagnosticResultDetailPanelProps) {
  const signedAt = result.verifiedAt ?? result.resultFinalizedAt;
  const descriptionLines = splitResultText(result.resultText);
  const reportUrl = result.reportFileUrl;

  return (
    <section
      aria-label="Phiếu kết quả CLS"
      data-testid="diagnostic-result-detail-panel"
      className="overflow-hidden rounded-xl border border-border bg-white"
    >
      <header className="bg-navy px-5 py-4 text-white">
        <p className="text-[11px] font-semibold uppercase tracking-wide text-white/70">
          {result.serviceCode} • {result.orderCode}
          {signedAt ? ` • Ký số lúc ${formatResultDateTime(signedAt)}` : ''}
        </p>
        <h3 className="mt-1 text-base font-bold">{result.serviceName}</h3>
        <p className="mt-1 text-xs text-white/80">
          BS thực hiện: {result.enteredByStaffName ?? '—'}
          {result.verifiedByStaffName ? ` • Người duyệt: ${result.verifiedByStaffName}` : ''}
        </p>
      </header>

      <div className="space-y-5 p-5">
        {!result.isResultReady ? (
          <div data-testid="diagnostic-result-not-ready">
            <Alert tone="warning">
              Kết quả của dịch vụ này chưa sẵn sàng (trạng thái: {result.resultStatus}).
            </Alert>
          </div>
        ) : (
          <>
            <div>
              <h4 className="text-xs font-bold uppercase tracking-wide text-navy">Kết luận</h4>
              <p className="mt-1 text-sm text-ink">{result.conclusion ?? '—'}</p>
            </div>

            <div>
              <h4 className="text-xs font-bold uppercase tracking-wide text-navy">
                MÔ TẢ KẾT QUẢ CHI TIẾT
              </h4>
              {descriptionLines.length > 0 ? (
                <ul className="mt-1 list-disc space-y-1 pl-5 text-sm text-ink">
                  {descriptionLines.map((line, lineIndex) => (
                    <li key={`${lineIndex}-${line}`}>{line}</li>
                  ))}
                </ul>
              ) : (
                <p className="mt-1 text-sm text-ink">Không có mô tả chi tiết.</p>
              )}
            </div>

            {result.instruction ? (
              <div className="rounded-lg border border-amber-200 bg-amber-50 p-4">
                <h4 className="text-xs font-bold uppercase tracking-wide text-amber-800">
                  CHỈ ĐỊNH CỦA BÁC SĨ
                </h4>
                <p className="mt-1 text-sm text-amber-900">{result.instruction}</p>
              </div>
            ) : null}

            {result.correctionReason ? (
              <Alert tone="warning">Đã hiệu chỉnh kết quả: {result.correctionReason}</Alert>
            ) : null}

            {result.versions.length > 1 ? (
              <div>
                <h4 className="text-xs font-bold uppercase tracking-wide text-navy">
                  LỊCH SỬ PHIÊN BẢN
                </h4>
                <ul className="mt-1 list-disc space-y-1 pl-5 text-sm text-ink">
                  {result.versions.map((version) => (
                    <li key={version.versionId}>
                      Phiên bản {version.versionNo} • {formatResultDateTime(version.enteredAt)} •{' '}
                      {version.enteredByStaffName ?? '—'}
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}

            {isSafeReportUrl(reportUrl) ? (
              <a
                href={reportUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-3 py-2 text-xs font-semibold text-navy transition-colors hover:bg-canvas"
              >
                <FileDown className="h-4 w-4" aria-hidden="true" />
                <span>Tải file PDF kết quả gốc</span>
              </a>
            ) : null}
          </>
        )}
      </div>
    </section>
  );
}
