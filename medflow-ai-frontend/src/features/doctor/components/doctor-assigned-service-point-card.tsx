import { Megaphone, Stethoscope } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type { DoctorAssignedServicePointDto } from '../types/doctor-assigned-queue.types';
import {
  ARRIVAL_CLASS_META,
  INELIGIBLE_REASON_META,
  PRESENCE_META,
  PRIORITY_META,
  QUEUE_GROUP_META,
} from '../utils/doctor-assigned-queue-meta';

interface DoctorAssignedServicePointCardProps {
  servicePoint: DoctorAssignedServicePointDto;
  onCallNext?: (servicePoint: DoctorAssignedServicePointDto) => void;
  isCallingNext?: boolean;
}

export function DoctorAssignedServicePointCard({
  servicePoint,
  onCallNext,
  isCallingNext,
}: DoctorAssignedServicePointCardProps) {
  return (
    <Card>
      <CardHeader className="flex flex-col md:flex-row md:items-center justify-between gap-3">
        <div className="flex items-start gap-3">
          <div className="rounded-xl bg-primary/10 text-primary px-2.5 py-1.5 font-mono text-xs font-extrabold">
            {servicePoint.roomCode}
          </div>
          <div>
            <div className="text-sm font-bold text-navy flex flex-wrap items-center gap-2">
              <span>{servicePoint.roomName}</span>
              <Badge tone="slate">{servicePoint.departmentName}</Badge>
              {!servicePoint.isActive && <Badge tone="rose">Tạm ngừng</Badge>}
            </div>
            <div className="text-[11px] text-sub mt-0.5 flex items-center gap-1.5">
              <Stethoscope className="w-3.5 h-3.5 text-primary" aria-hidden="true" />
              <span>{servicePoint.servicePointName}</span>
            </div>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-3 bg-canvas rounded-xl px-3.5 py-2 border border-border">
          <div>
            <div className="text-[10px] font-semibold uppercase text-sub">Đang phục vụ</div>
            <div className="font-mono text-sm font-extrabold text-emerald-700">
              {servicePoint.currentServingTicketNo ?? 'Chưa gọi'}
            </div>
          </div>
          <div className="h-6 w-px bg-border" />
          <div>
            <div className="text-[10px] font-semibold uppercase text-sub">Kế tiếp đủ điều kiện</div>
            <div className="font-mono text-sm font-bold text-navy">
              {servicePoint.nextEligibleTicketNo ?? '—'}
            </div>
          </div>
          <div className="h-6 w-px bg-border" />
          <div className="text-[11px] text-sub font-medium">
            {servicePoint.stats.eligibleCount} đủ điều kiện • {servicePoint.stats.notEligibleCount}{' '}
            chưa đủ • {servicePoint.stats.completedCount} xong
          </div>
          {onCallNext && (
            <Button
              type="button"
              variant="primary"
              size="sm"
              loading={Boolean(isCallingNext)}
              disabled={!servicePoint.isActive || Boolean(isCallingNext)}
              onClick={() => onCallNext(servicePoint)}
              aria-label={`Gọi bệnh nhân tiếp theo tại ${servicePoint.roomCode}`}
            >
              <Megaphone className="w-3.5 h-3.5" aria-hidden="true" />
              Gọi bệnh nhân tiếp theo
            </Button>
          )}
        </div>
      </CardHeader>

      <CardContent className="p-0 px-6 pb-4">
        <div className="overflow-x-auto">
          <table aria-label={`Hàng đợi ${servicePoint.roomCode}`} className="w-full text-xs">
            <thead>
              <tr className="text-left text-[11px] text-sub uppercase border-b border-border">
                <th className="py-2.5 font-semibold">Thứ tự</th>
                <th className="py-2.5 font-semibold">Số phiếu / Mã lượt</th>
                <th className="py-2.5 font-semibold">Bệnh nhân</th>
                <th className="py-2.5 font-semibold">Loại & Hiện diện</th>
                <th className="py-2.5 font-semibold">Thời gian</th>
                <th className="py-2.5 font-semibold">Ưu tiên</th>
                <th className="py-2.5 font-semibold">Nhóm hàng đợi</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {servicePoint.entries.map((entry) => {
                const groupMeta = entry.queueGroup ? QUEUE_GROUP_META[entry.queueGroup] : null;
                const reasonMeta = entry.ineligibleReason
                  ? INELIGIBLE_REASON_META[entry.ineligibleReason]
                  : null;
                const priorityMeta = PRIORITY_META[entry.priority];
                const presenceMeta = PRESENCE_META[entry.presenceStatus];
                const arrivalMeta = ARRIVAL_CLASS_META[entry.arrivalClass];
                const isServingOrCalled =
                  entry.queueGroup === 'IN_PROGRESS' || entry.queueGroup === 'CALLED';
                const isMuted = entry.queueGroup === 'SKIPPED' || entry.queueGroup === 'COMPLETED';

                const rowClass = isServingOrCalled
                  ? 'bg-emerald-50/60 hover:bg-emerald-50'
                  : isMuted
                    ? 'opacity-70 hover:bg-canvas/60'
                    : 'hover:bg-canvas/60';

                return (
                  <tr key={entry.id} className={rowClass}>
                    <td className="py-3">
                      <span
                        className={`inline-flex items-center justify-center rounded-lg px-2 py-1 font-mono text-xs font-extrabold ${
                          isServingOrCalled
                            ? 'bg-emerald-100 text-emerald-800'
                            : 'bg-canvas text-navy'
                        }`}
                      >
                        #{entry.servingOrder}
                      </span>
                    </td>
                    <td className="py-3">
                      <div className="font-mono font-bold text-navy">{entry.ticketNo}</div>
                      <div className="text-[10px] font-mono text-sub">{entry.visitCode}</div>
                    </td>
                    <td className="py-3">
                      <div className="font-semibold text-ink">{entry.patientName}</div>
                      <div className="text-[10px] text-sub font-mono">{entry.patientCode}</div>
                    </td>
                    <td className="py-3">
                      <div className="flex flex-wrap items-center gap-1">
                        {entry.queueType === 'RETURN_CONSULTATION' && (
                          <Badge tone="emerald">Đọc KQ CLS</Badge>
                        )}
                        <Badge tone={entry.visitType === 'SCHEDULED' ? 'sky' : 'slate'}>
                          {entry.visitType === 'SCHEDULED' ? 'Hẹn trước' : 'Walk-in'}
                        </Badge>
                        <Badge tone={presenceMeta.tone}>{presenceMeta.label}</Badge>
                        <Badge tone={arrivalMeta.tone}>{arrivalMeta.label}</Badge>
                      </div>
                    </td>
                    <td className="py-3 text-sub font-mono tabular-nums">
                      <div>Check-in: {entry.checkedInAt}</div>
                      {entry.calledAt ? (
                        <div className="text-[10px] text-emerald-700">Gọi: {entry.calledAt}</div>
                      ) : entry.scheduledAt ? (
                        <div className="text-[10px]">Hẹn: {entry.scheduledAt}</div>
                      ) : null}
                    </td>
                    <td className="py-3">
                      <Badge tone={priorityMeta.tone}>{priorityMeta.label}</Badge>
                    </td>
                    <td className="py-3">
                      <div className="flex flex-col items-start gap-1">
                        {groupMeta && <Badge tone={groupMeta.tone}>{groupMeta.label}</Badge>}
                        {reasonMeta && <Badge tone={reasonMeta.tone}>{reasonMeta.label}</Badge>}
                        {entry.blockedReason && (
                          <div className="text-[10px] text-rose-600 max-w-44">
                            {entry.blockedReason}
                          </div>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </CardContent>
    </Card>
  );
}
