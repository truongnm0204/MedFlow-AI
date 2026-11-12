import { Badge } from '@/shared/ui/badge';
import { Modal } from '@/shared/ui/modal';

import type {
  DiagnosticTrackingOrderStatus,
  DiagnosticTrackingVisitDto,
} from '../types/doctor-diagnostic-tracking.types';

interface DoctorDiagnosticVisitSummaryModalProps {
  visit: DiagnosticTrackingVisitDto | null;
  onClose: () => void;
}

const ORDER_STATUS_BADGE: Record<
  DiagnosticTrackingOrderStatus,
  { label: string; tone: 'emerald' | 'amber' | 'sky' | 'slate' }
> = {
  SIGNED: { label: 'Đã ký duyệt', tone: 'emerald' },
  AWAITING_SIGN: { label: 'Chờ ký duyệt', tone: 'amber' },
  IN_PROGRESS: { label: 'Đang thực hiện', tone: 'sky' },
  PENDING: { label: 'Chờ thực hiện', tone: 'slate' },
};

function formatSignedInfo(signedAt: string | null, signedByStaffName: string | null): string {
  if (!signedAt) return '—';
  const parsed = new Date(signedAt);
  const formattedTime = Number.isNaN(parsed.getTime())
    ? signedAt
    : parsed.toLocaleString('vi-VN', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      });
  if (signedByStaffName && signedByStaffName.trim() !== '') {
    return `${formattedTime} • ${signedByStaffName}`;
  }
  return formattedTime;
}

export function DoctorDiagnosticVisitSummaryModal({
  visit,
  onClose,
}: DoctorDiagnosticVisitSummaryModalProps) {
  return (
    <Modal open={visit !== null} onClose={onClose} maxWidth="max-w-3xl">
      {visit && (
        <div className="p-6">
          <h2 className="pr-8 text-base font-bold text-navy">
            Tổng hợp kết quả CLS • {visit.visitCode}
          </h2>
          <p className="mt-0.5 text-xs text-sub">
            {visit.patientName ?? '—'} ({visit.patientCode ?? '—'}) • Hoàn thành{' '}
            {visit.signedOrders}/{visit.totalOrders} ({visit.completionPercent}%)
          </p>

          <ul className="mt-4 max-h-[60vh] space-y-3 overflow-y-auto">
            {visit.orders.map((order) => {
              const badge = ORDER_STATUS_BADGE[order.trackingStatus];
              const isHighPriority = order.priority === 'URGENT' || order.priority === 'STAT';
              return (
                <li
                  key={order.orderId}
                  data-testid={`summary-order-${order.orderCode}`}
                  className="rounded-xl border border-border p-3"
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <div className="text-sm font-semibold text-ink">
                        {order.serviceName ?? order.serviceCode ?? order.orderCode}
                      </div>
                      <div className="font-mono text-[10px] text-sub">{order.orderCode}</div>
                    </div>
                    <div className="flex flex-wrap items-center gap-1.5">
                      {order.requiredForConclusion && <Badge tone="slate">Bắt buộc</Badge>}
                      {isHighPriority && <Badge tone="rose">Ưu tiên {order.priority}</Badge>}
                      <Badge tone={badge.tone}>{badge.label}</Badge>
                    </div>
                  </div>
                  <dl className="mt-2 grid grid-cols-1 gap-2 text-xs sm:grid-cols-2 lg:grid-cols-4">
                    <div>
                      <dt className="text-[10px] font-bold uppercase text-sub">BS chỉ định</dt>
                      <dd className="text-ink">{order.orderingDoctorName ?? '—'}</dd>
                    </div>
                    <div>
                      <dt className="text-[10px] font-bold uppercase text-sub">Người thực hiện</dt>
                      <dd className="text-ink">{order.assigneeStaffName ?? 'Chưa phân công'}</dd>
                    </div>
                    <div>
                      <dt className="text-[10px] font-bold uppercase text-sub">Tóm tắt kết quả</dt>
                      <dd className="text-ink">{order.resultSummary ?? 'Chưa có kết quả'}</dd>
                    </div>
                    <div>
                      <dt className="text-[10px] font-bold uppercase text-sub">Ký duyệt</dt>
                      <dd className="text-ink">
                        {formatSignedInfo(order.signedAt, order.signedByStaffName)}
                      </dd>
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
