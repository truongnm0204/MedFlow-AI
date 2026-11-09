import { Clock, ListOrdered } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type { ConsultationQueueContextDto } from '../types/consultation-workspace.types';
import {
  ARRIVAL_CLASS_META,
  INELIGIBLE_REASON_META,
  PRESENCE_META,
  PRIORITY_META,
  QUEUE_GROUP_META,
} from '../utils/doctor-assigned-queue-meta';

export interface ConsultationQueueContextCardProps {
  queue: ConsultationQueueContextDto | null;
}

export function ConsultationQueueContextCard({ queue }: ConsultationQueueContextCardProps) {
  if (!queue) {
    return (
      <Card>
        <CardHeader className="flex items-center gap-2">
          <ListOrdered className="h-4 w-4 text-primary" aria-hidden="true" />
          <h3 className="text-sm font-bold text-navy">Ngữ cảnh hàng đợi (UC30)</h3>
        </CardHeader>
        <CardContent className="text-xs text-sub">
          Lượt khám hiện chưa có bản ghi hàng đợi lâm sàng hoạt động.
        </CardContent>
      </Card>
    );
  }

  const groupMeta = QUEUE_GROUP_META[queue.queueGroup] ?? {
    label: queue.queueGroup,
    tone: 'slate' as const,
  };
  const priorityMeta = PRIORITY_META[queue.priority] ?? {
    label: queue.priority,
    tone: 'slate' as const,
  };
  const presenceMeta = PRESENCE_META[queue.presenceStatus] ?? {
    label: queue.presenceStatus,
    tone: 'slate' as const,
  };
  const arrivalMeta = ARRIVAL_CLASS_META[queue.arrivalClass] ?? {
    label: queue.arrivalClass,
    tone: 'slate' as const,
  };
  const ineligibleMeta = queue.ineligibleReason
    ? INELIGIBLE_REASON_META[queue.ineligibleReason]
    : null;

  return (
    <Card>
      <CardHeader className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <ListOrdered className="h-4 w-4 text-primary" aria-hidden="true" />
          <h3 className="text-sm font-bold text-navy">Ngữ cảnh hàng đợi (UC30)</h3>
        </div>
        <span className="font-mono text-xs font-bold text-sub">v{queue.stateVersion}</span>
      </CardHeader>

      <CardContent className="space-y-4 text-xs">
        <div className="flex items-center justify-between rounded-xl bg-canvas border border-border p-3">
          <div>
            <div className="text-[10px] font-semibold uppercase text-sub">
              Số phiếu / Mã hàng đợi
            </div>
            <div className="font-mono text-sm font-extrabold text-navy">
              {queue.ticketNo}{' '}
              <span className="text-xs font-normal text-sub">({queue.formattedQueueEntryId})</span>
            </div>
          </div>
          <div className="text-right">
            <div className="text-[10px] font-semibold uppercase text-sub">Thứ tự phục vụ</div>
            <div className="font-mono text-sm font-extrabold text-primary">
              #{queue.servingOrder}
            </div>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-1.5">
          <Badge tone={groupMeta.tone}>{groupMeta.label}</Badge>
          <Badge tone={priorityMeta.tone}>{priorityMeta.label}</Badge>
          <Badge tone={presenceMeta.tone}>{presenceMeta.label}</Badge>
          <Badge tone={arrivalMeta.tone}>{arrivalMeta.label}</Badge>
          {queue.queueType === 'RETURN_CONSULTATION' && <Badge tone="emerald">Đọc KQ CLS</Badge>}
          {queue.isAppointmentProtected && <Badge tone="sky">Bảo vệ giờ hẹn</Badge>}
          {ineligibleMeta && <Badge tone={ineligibleMeta.tone}>{ineligibleMeta.label}</Badge>}
        </div>

        {(queue.blockedReason || queue.holdReason) && (
          <div className="rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-[11px] text-amber-900">
            {queue.blockedReason ?? queue.holdReason}
          </div>
        )}

        <div className="rounded-xl bg-canvas border border-border p-3 space-y-1.5">
          <div className="text-[11px] font-semibold text-navy flex items-center gap-1.5">
            <Clock className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
            <span>Mốc thời gian tại {queue.servicePointName}</span>
          </div>
          <div className="grid grid-cols-2 gap-2 font-mono text-[11px] text-sub">
            <div>Giờ hẹn: {queue.scheduledAt ?? '—'}</div>
            <div>Check-in: {queue.arrivedAt ?? '—'}</div>
            <div>Đã gọi: {queue.calledAt ?? '—'}</div>
            <div>Bắt đầu: {queue.startedAt ?? '—'}</div>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
