import { AlertTriangle } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Modal } from '@/shared/ui/modal';

import type {
  TrackingOrderStatus,
  TrackingVisit,
} from '../types/diagnostic-results-tracking.types';

interface DiagnosticVisitSummaryModalProps {
  visit: TrackingVisit | null;
  onClose: () => void;
}

const ORDER_STATUS_BADGE: Record<
  TrackingOrderStatus,
  { label: string; tone: 'emerald' | 'sky' | 'slate' }
> = {
  SIGNED: { label: 'SIGNED', tone: 'emerald' },
  IN_PROGRESS: { label: 'IN_PROGRESS', tone: 'sky' },
  PENDING: { label: 'PENDING', tone: 'slate' },
};

export function DiagnosticVisitSummaryModal({ visit, onClose }: DiagnosticVisitSummaryModalProps) {
  return (
    <Modal open={visit !== null} onClose={onClose} maxWidth="max-w-3xl">
      {visit && (
        <div className="p-6">
          <h2 className="pr-8 text-base font-bold text-navy">
            Tổng hợp kết quả CLS • {visit.visitCode}
          </h2>
          <p className="mt-0.5 text-xs text-sub">
            {visit.patientName} ({visit.patientCode}) • {visit.orderingDoctor}
          </p>

          <ul className="mt-4 max-h-[60vh] space-y-3 overflow-y-auto">
            {visit.orders.map((order) => {
              const badge = ORDER_STATUS_BADGE[order.status];
              return (
                <li
                  key={order.id}
                  data-testid={`summary-order-${order.id}`}
                  className="rounded-xl border border-border p-3"
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <div className="text-sm font-semibold text-ink">{order.serviceName}</div>
                      <div className="font-mono text-[10px] text-sub">{order.orderCode}</div>
                    </div>
                    <div className="flex items-center gap-2">
                      {order.isAbnormal && (
                        <Badge tone="rose">
                          <AlertTriangle className="mr-1 h-3 w-3" aria-hidden="true" />
                          Bất thường
                        </Badge>
                      )}
                      <Badge tone={badge.tone}>{badge.label}</Badge>
                    </div>
                  </div>
                  <dl className="mt-2 grid grid-cols-1 gap-1 text-xs sm:grid-cols-3">
                    <div>
                      <dt className="text-[10px] font-bold uppercase text-sub">Người thực hiện</dt>
                      <dd className="text-ink">{order.assignee}</dd>
                    </div>
                    <div>
                      <dt className="text-[10px] font-bold uppercase text-sub">Tóm tắt kết quả</dt>
                      <dd className="text-ink">{order.resultSummary ?? 'Chưa có kết quả'}</dd>
                    </div>
                    <div>
                      <dt className="text-[10px] font-bold uppercase text-sub">
                        Thời gian ký duyệt
                      </dt>
                      <dd className="text-ink">{order.signedAt ?? '—'}</dd>
                    </div>
                  </dl>
                </li>
              );
            })}
          </ul>

          <div className="mt-5 flex justify-end">
            <button
              type="button"
              aria-label="Đóng tổng hợp kết quả"
              onClick={onClose}
              className="rounded-lg border border-border px-4 py-2 text-xs font-semibold text-navy transition-colors hover:bg-canvas"
            >
              Đóng
            </button>
          </div>
        </div>
      )}
    </Modal>
  );
}
