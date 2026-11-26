import { ListOrdered, Printer, QrCode } from 'lucide-react';
import { useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Spinner } from '@/shared/ui/spinner';

import { fetchJourneySlipApi, printJourneySlipApi } from '../api/journey-slip.api';

export interface JourneySlipProps {
  /** Visit id or code (e.g. `VIS-202611-0001`). */
  visitIdOrCode: string;
}

const TICKET_STATUS_BADGE: Record<string, { label: string; tone: 'emerald' | 'amber' | 'slate' | 'sky' }> = {
  WAITING: { label: 'Chờ', tone: 'sky' },
  READY: { label: 'Sẵn sàng', tone: 'emerald' },
  CALLED: { label: 'Đã gọi', tone: 'emerald' },
  IN_PROGRESS: { label: 'Đang thực hiện', tone: 'emerald' },
  COMPLETED: { label: 'Hoàn tất', tone: 'slate' },
  DEFERRED: { label: 'Tạm hoãn', tone: 'amber' },
  CANCELLED: { label: 'Đã hủy', tone: 'slate' },
  NO_SHOW: { label: 'Không đến', tone: 'slate' },
};

/**
 * Minimal operational journey slip (QE-21 / E2-R09): ordered queue tickets + internal lookup QR.
 * Contains no service names, payment data, or PHI — safe to print for the patient.
 */
export function JourneySlip({ visitIdOrCode }: JourneySlipProps) {
  const [printNote, setPrintNote] = useState<string | null>(null);

  const slipQuery = useQuery({
    queryKey: ['journey-slip', visitIdOrCode],
    queryFn: () => fetchJourneySlipApi(visitIdOrCode),
    enabled: Boolean(visitIdOrCode),
    retry: false,
  });

  const printMutation = useMutation({
    mutationFn: () => printJourneySlipApi(visitIdOrCode),
    onSuccess: (result) => {
      setPrintNote(result.note);
      window.print();
    },
  });

  const slip = slipQuery.data ?? null;
  const error = slipQuery.isError
    ? getErrorMessage(slipQuery.error)
    : printMutation.isError
      ? getErrorMessage(printMutation.error)
      : null;


  if (slipQuery.isPending) {
    return (
      <div className="flex items-center gap-2 text-sm text-sub">
        <Spinner /> Đang tải phiếu hành trình…
      </div>
    );
  }

  if (!slip) {
    return <Alert tone="error">{error ?? 'Không tải được phiếu hành trình.'}</Alert>;
  }

  return (
    <div className="space-y-4" data-testid="journey-slip">
      <header className="flex items-start justify-between gap-3">
        <div>
          <h3 className="text-base font-semibold text-ink">Phiếu hành trình khám</h3>
          <p className="text-sm text-sub">
            {slip.visitCode} · Ngày khám {slip.servingDate}
          </p>
        </div>
        <Button
          type="button"
          onClick={() => printMutation.mutate()}
          disabled={printMutation.isPending}
        >
          <Printer className="mr-1 size-4" aria-hidden="true" />
          In phiếu
        </Button>
      </header>

      {error && <Alert tone="error">{error}</Alert>}
      {printNote && <Alert tone="success">{printNote}</Alert>}

      {(slip.currentAssignedDoctorDisplay || slip.currentExaminationRoomDisplay) && (
        <div className="rounded-lg border border-border bg-surface p-3 text-sm text-sub">
          {slip.currentAssignedDoctorDisplay && (
            <p>
              Bác sĩ: <span className="font-medium text-ink">{slip.currentAssignedDoctorDisplay}</span>
            </p>
          )}
          {slip.currentExaminationRoomDisplay && (
            <p>
              Phòng: <span className="font-medium text-ink">{slip.currentExaminationRoomDisplay}</span>
            </p>
          )}
        </div>
      )}

      <div>
        <h4 className="mb-2 flex items-center gap-2 text-sm font-medium text-ink">
          <ListOrdered className="size-4" aria-hidden="true" />
          Trình tự các điểm khám
        </h4>
        {slip.tickets.length === 0 ? (
          <p className="text-sm text-sub">Chưa có điểm khám nào được cấp số.</p>
        ) : (
          <ol className="space-y-1">
            {slip.tickets.map((t, i) => {
              const badge = TICKET_STATUS_BADGE[t.status] ?? { label: t.status, tone: 'slate' as const };
              return (
                <li
                  key={t.queueEntryId ?? `${t.queueType}-${i}`}
                  className="flex items-center justify-between rounded-lg border border-border bg-canvas px-3 py-2 text-sm"
                >
                  <div className="flex items-center gap-3">
                    <span className="flex size-6 items-center justify-center rounded-full bg-primary/10 text-xs font-semibold text-primary">
                      {i + 1}
                    </span>
                    <div>
                      <span className="font-medium text-ink">Số {t.ticketNo}</span>
                      <span className="ml-2 text-sub">
                        {t.servicePointName ?? t.queueType}
                      </span>
                    </div>
                  </div>
                  <Badge tone={badge.tone}>{badge.label}</Badge>
                </li>
              );
            })}
          </ol>
        )}
      </div>

      <div className="flex items-center gap-3 rounded-lg border border-dashed border-border p-3">
        <QrCode className="size-8 text-ink" aria-hidden="true" />
        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-sub">Mã tra cứu nội bộ</p>
          <p className="font-mono text-sm text-ink" data-testid="journey-slip-qr">
            {slip.internalLookupQr}
          </p>
        </div>
      </div>
    </div>
  );
}

export interface JourneySlipViewerProps {
  /** Preload a specific visit; when omitted the viewer shows a visit-code lookup. */
  initialVisitCode?: string | null;
}

/** Journey-slip viewer with an optional visit-code lookup (staff print path). */
export function JourneySlipViewer({ initialVisitCode }: JourneySlipViewerProps) {
  const [searchInput, setSearchInput] = useState(initialVisitCode?.trim() ?? '');
  const [activeKey, setActiveKey] = useState(initialVisitCode?.trim() ?? '');

  return (
    <div className="space-y-4">
      {!activeKey && (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            const key = searchInput.trim();
            if (key) setActiveKey(key);
          }}
          className="flex gap-2"
        >
          <input
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            placeholder="Nhập mã lượt khám (vd: VIS-202611-0001)"
            className="flex-1 rounded-lg border border-border bg-canvas px-3 py-2 text-sm text-ink"
            aria-label="Mã lượt khám"
          />
          <Button type="submit">Tra cứu</Button>
        </form>
      )}
      {activeKey && <JourneySlip visitIdOrCode={activeKey} />}
    </div>
  );
}
