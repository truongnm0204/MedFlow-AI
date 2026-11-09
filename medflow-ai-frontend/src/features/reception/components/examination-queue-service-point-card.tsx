import { FileCheck2, Megaphone, Stethoscope, XCircle } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type {
  CallNextSelectionReason,
  ExaminationQueueEntryDto,
  ExaminationQueueIneligibleReason,
  ExaminationQueuePriority,
  ExaminationQueueStatus,
  QueueArrivalClass,
  QueuePresenceStatus,
  ReturnToDoctorModalMode,
  ServicePointQueueDto,
} from '../types/examination-queue.types';
import { CANCELLABLE_QUEUE_STATUSES } from '../types/queue-status-update.types';

export interface ExaminationQueueServicePointCardProps {
  servicePoint: ServicePointQueueDto;
  canManageQueue: boolean;
  onSelectPatient: (patientCode: string) => void;
  onOpenEarlyArrivalBackfill?: (
    entry: ExaminationQueueEntryDto,
    servicePoint: ServicePointQueueDto,
  ) => void;
  onUpdateStatus?: (
    entry: ExaminationQueueEntryDto,
    initialTargetStatus?: ExaminationQueueStatus,
  ) => void;
  onApplyPriority?: (entry: ExaminationQueueEntryDto) => void;
  onOpenMissedTurn?: (entry: ExaminationQueueEntryDto, servicePoint: ServicePointQueueDto) => void;
  onOpenRestoreTurn?: (entry: ExaminationQueueEntryDto, servicePoint: ServicePointQueueDto) => void;
  onOpenCancelEntry?: (entry: ExaminationQueueEntryDto, servicePoint: ServicePointQueueDto) => void;
  onOpenReturnToDoctor?: (
    entry: ExaminationQueueEntryDto,
    servicePoint: ServicePointQueueDto,
    mode: ReturnToDoctorModalMode,
  ) => void;
  onCallNext?: (servicePoint: ServicePointQueueDto) => void;
  isCallingNext?: boolean;
}

// oxlint-disable-next-line react/only-export-components
export const EXAMINATION_QUEUE_STATUS_META: Record<
  ExaminationQueueStatus,
  { label: string; tone: 'amber' | 'sky' | 'emerald' | 'slate' | 'rose' }
> = {
  WAITING: { label: 'Đang chờ', tone: 'amber' },
  READY: { label: 'Sẵn sàng', tone: 'sky' },
  CALLED: { label: 'Đã gọi số', tone: 'sky' },
  IN_PROGRESS: { label: 'Đang khám', tone: 'emerald' },
  COMPLETED: { label: 'Hoàn tất', tone: 'slate' },
  SKIPPED: { label: 'Qua lượt', tone: 'rose' },
};

// oxlint-disable-next-line react/only-export-components
export const EXAMINATION_QUEUE_PRIORITY_META: Record<
  ExaminationQueuePriority,
  { label: string; tone: 'slate' | 'amber' | 'sky' }
> = {
  NORMAL: { label: 'Bình thường', tone: 'slate' },
  ELDERLY: { label: 'Người cao tuổi', tone: 'amber' },
  PREGNANT: { label: 'Thai phụ', tone: 'sky' },
};

// oxlint-disable-next-line react/only-export-components
export const PRESENCE_STATUS_META: Record<
  QueuePresenceStatus,
  { label: string; tone: 'emerald' | 'amber' | 'rose' }
> = {
  PRESENT: { label: 'Có mặt', tone: 'emerald' },
  NOT_ARRIVED: { label: 'Chưa đến', tone: 'amber' },
  LEFT: { label: 'Vắng mặt', tone: 'rose' },
};

// oxlint-disable-next-line react/only-export-components
export const ARRIVAL_CLASS_META: Record<
  QueueArrivalClass,
  { label: string; tone: 'emerald' | 'sky' | 'amber' | 'slate' }
> = {
  ON_TIME_OR_GRACE: { label: 'Đúng giờ', tone: 'emerald' },
  EARLY: { label: 'Đến sớm', tone: 'sky' },
  LATE: { label: 'Đến muộn', tone: 'amber' },
  WALK_IN: { label: 'Vãng lai', tone: 'slate' },
};

// oxlint-disable-next-line react/only-export-components
export const EXAMINATION_QUEUE_INELIGIBLE_REASON_META: Record<
  ExaminationQueueIneligibleReason,
  { label: string; tone: 'amber' | 'rose' }
> = {
  NOT_ARRIVED: { label: 'Chưa đến', tone: 'amber' },
  LEFT: { label: 'Đã rời khu chờ', tone: 'rose' },
  BLOCKED: { label: 'Giữ chỗ theo giờ hẹn', tone: 'amber' },
  AWAITING_RESULTS: { label: 'Chờ kết quả CLS', tone: 'rose' },
};

// oxlint-disable-next-line react/only-export-components
export const CALL_NEXT_SELECTION_REASON_META: Record<
  CallNextSelectionReason,
  { label: string; description: string; tone: 'emerald' | 'amber' | 'sky' | 'slate' }
> = {
  APPOINTMENT_DUE: {
    label: 'Đúng giờ hẹn',
    description: 'Lịch hẹn được bảo vệ đã đến giờ khám',
    tone: 'emerald',
  },
  RETURN_CONSULTATION: {
    label: 'Đọc KQ CLS',
    description: 'Bệnh nhân quay lại đọc kết quả cận lâm sàng đã sẵn sàng',
    tone: 'emerald',
  },
  PRIORITY: {
    label: 'Ưu tiên',
    description: 'Nhóm ưu tiên (Người cao tuổi / Thai phụ) theo tỷ lệ luân phiên công bằng',
    tone: 'amber',
  },
  SAFE_BACKFILL: {
    label: 'Safe Backfill',
    description: 'Khám sớm an toàn trong khoảng trống trước lịch hẹn được bảo vệ kế tiếp',
    tone: 'sky',
  },
  FIFO: {
    label: 'FIFO',
    description: 'Thứ tự sẵn sàng khám theo quy tắc công bằng (FIFO)',
    tone: 'slate',
  },
};

export function ExaminationQueueServicePointCard({
  servicePoint,
  canManageQueue,
  onSelectPatient,
  onOpenEarlyArrivalBackfill,
  onUpdateStatus,
  onApplyPriority,
  onOpenMissedTurn,
  onOpenRestoreTurn,
  onOpenCancelEntry,
  onOpenReturnToDoctor,
  onCallNext,
  isCallingNext,
}: ExaminationQueueServicePointCardProps) {
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
              <span>{servicePoint.doctorName}</span>
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
            <div className="text-[10px] font-semibold uppercase text-sub">Số kế tiếp</div>
            <div className="font-mono text-sm font-bold text-navy">
              {servicePoint.nextTicketNo ?? '—'}
            </div>
          </div>
          <div className="h-6 w-px bg-border" />
          <div className="text-[11px] text-sub font-medium">
            {servicePoint.waitingCount} chờ • {servicePoint.inProgressCount} đang khám •{' '}
            {servicePoint.completedCount} xong
          </div>
          {canManageQueue && onCallNext && (
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
          <table className="w-full text-xs">
            <thead>
              <tr className="text-left text-[11px] text-sub uppercase border-b border-border">
                <th className="py-2.5 font-semibold">Thứ tự</th>
                <th className="py-2.5 font-semibold">Số phiếu / Mã lượt</th>
                <th className="py-2.5 font-semibold">Bệnh nhân</th>
                <th className="py-2.5 font-semibold">Loại & Hiện diện</th>
                <th className="py-2.5 font-semibold">Thời gian</th>
                <th className="py-2.5 font-semibold">Ưu tiên</th>
                <th className="py-2.5 font-semibold">Trạng thái</th>
                {canManageQueue && <th className="py-2.5 font-semibold text-right">Thao tác</th>}
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {servicePoint.entries.map((entry) => {
                const statusMeta = EXAMINATION_QUEUE_STATUS_META[entry.status];
                const priorityMeta = EXAMINATION_QUEUE_PRIORITY_META[entry.priority];
                const presenceMeta = PRESENCE_STATUS_META[entry.presenceStatus];
                const arrivalMeta = ARRIVAL_CLASS_META[entry.arrivalClass];
                const isServingOrCalled =
                  entry.status === 'IN_PROGRESS' || entry.status === 'CALLED';

                return (
                  <tr key={entry.id} className="hover:bg-canvas/60">
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
                      <button
                        type="button"
                        onClick={() => onSelectPatient(entry.patientCode)}
                        aria-label={`Xem chi tiết bệnh nhân ${entry.patientName}`}
                        className="text-left group focus:outline-none"
                      >
                        <div className="font-semibold text-ink group-hover:text-primary group-hover:underline transition-colors">
                          {entry.patientName}
                        </div>
                        <div className="text-[10px] text-sub font-mono">{entry.patientCode}</div>
                      </button>
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
                        {entry.visitType === 'SCHEDULED' &&
                          (entry.arrivalClass === 'LATE' ||
                            entry.isAppointmentProtected === false) && (
                            <Badge tone="amber">Mất bảo vệ giờ hẹn</Badge>
                          )}
                        {entry.isBackfilled && <Badge tone="emerald">Safe Backfill</Badge>}
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
                      <div className="flex flex-wrap items-center gap-1">
                        <Badge tone={statusMeta.tone}>{statusMeta.label}</Badge>
                        {entry.ineligibleReason && (
                          <Badge
                            tone={
                              EXAMINATION_QUEUE_INELIGIBLE_REASON_META[entry.ineligibleReason].tone
                            }
                          >
                            {EXAMINATION_QUEUE_INELIGIBLE_REASON_META[entry.ineligibleReason].label}
                          </Badge>
                        )}
                        {entry.queueType === 'RETURN_CONSULTATION' &&
                          entry.ineligibleReason !== 'AWAITING_RESULTS' &&
                          (entry.status === 'READY' ||
                            entry.status === 'CALLED' ||
                            entry.status === 'IN_PROGRESS' ||
                            entry.isEligible === true) && <Badge tone="emerald">Đủ KQ CLS</Badge>}
                      </div>
                      {entry.blockedReason && (
                        <div className="text-[10px] text-rose-600 mt-1 max-w-44">
                          {entry.blockedReason}
                        </div>
                      )}
                      {entry.isBackfilled && entry.backfillReason && (
                        <div className="text-[10px] text-emerald-700 mt-1 max-w-44">
                          {entry.backfillReason}
                        </div>
                      )}
                    </td>
                    {canManageQueue && (
                      <td className="py-3 text-right">
                        <div className="inline-flex flex-wrap items-center justify-end gap-1.5">
                          {onOpenEarlyArrivalBackfill &&
                            (entry.status === 'WAITING' || entry.status === 'READY') && (
                              <Button
                                variant="secondary"
                                size="sm"
                                onClick={() => onOpenEarlyArrivalBackfill(entry, servicePoint)}
                                aria-label={`Xử lý đến sớm hoặc Safe Backfill cho ${entry.patientName}`}
                              >
                                Xử lý đến sớm / Backfill
                              </Button>
                            )}
                          {onOpenReturnToDoctor &&
                            ((entry.queueType === 'EXAMINATION' && entry.status !== 'SKIPPED') ||
                              (entry.queueType === 'RETURN_CONSULTATION' &&
                                (entry.status === 'WAITING' ||
                                  (entry.status === 'READY' &&
                                    (entry.presenceStatus !== 'PRESENT' ||
                                      entry.ineligibleReason === 'AWAITING_RESULTS'))))) && (
                              <Button
                                variant="secondary"
                                size="sm"
                                onClick={() => {
                                  const isActivate = entry.queueType === 'RETURN_CONSULTATION';
                                  onOpenReturnToDoctor(
                                    entry,
                                    servicePoint,
                                    isActivate ? 'ACTIVATE_RETURN' : 'CREATE_RETURN',
                                  );
                                }}
                                aria-label={
                                  entry.queueType === 'RETURN_CONSULTATION'
                                    ? `Kích hoạt lượt khám lại đọc kết quả CLS cho ${entry.patientName}`
                                    : `Tạo lượt khám lại đọc kết quả CLS cho ${entry.patientName}`
                                }
                              >
                                <FileCheck2 className="w-3.5 h-3.5" aria-hidden="true" />
                                {entry.queueType === 'RETURN_CONSULTATION'
                                  ? 'Kích hoạt đọc KQ CLS'
                                  : 'Tạo lượt đọc KQ CLS'}
                              </Button>
                            )}
                          {onOpenMissedTurn && entry.status === 'CALLED' && (
                            <Button
                              variant="secondary"
                              size="sm"
                              onClick={() => onOpenMissedTurn(entry, servicePoint)}
                              aria-label={`Đánh dấu qua lượt khám cho ${entry.patientName}`}
                            >
                              Đánh dấu qua lượt
                            </Button>
                          )}
                          {onOpenRestoreTurn && entry.status === 'SKIPPED' && (
                            <Button
                              variant="secondary"
                              size="sm"
                              onClick={() => onOpenRestoreTurn(entry, servicePoint)}
                              aria-label={`Khôi phục lượt khám cho ${entry.patientName}`}
                            >
                              Khôi phục lượt khám
                            </Button>
                          )}
                          {onApplyPriority && (
                            <Button
                              variant="secondary"
                              size="sm"
                              disabled={
                                entry.status === 'IN_PROGRESS' || entry.status === 'COMPLETED'
                              }
                              aria-label={`Áp dụng ưu tiên khám ngoại trú cho ${entry.patientName}`}
                              onClick={() => onApplyPriority(entry)}
                            >
                              Áp dụng ưu tiên
                            </Button>
                          )}
                          {onUpdateStatus && (
                            <Button
                              variant="primary"
                              size="sm"
                              disabled={entry.status === 'COMPLETED'}
                              aria-label={`Cập nhật trạng thái hàng đợi cho ${entry.patientName}`}
                              onClick={() => onUpdateStatus(entry)}
                            >
                              Cập nhật TT
                            </Button>
                          )}
                          {onOpenCancelEntry &&
                            CANCELLABLE_QUEUE_STATUSES.includes(entry.status) && (
                              <Button
                                variant="danger"
                                size="sm"
                                onClick={() => onOpenCancelEntry(entry, servicePoint)}
                                aria-label={`Hủy lượt khám cho ${entry.patientName}`}
                              >
                                <XCircle className="w-3.5 h-3.5" aria-hidden="true" />
                                Hủy lượt
                              </Button>
                            )}
                          <Button
                            variant="secondary"
                            size="sm"
                            onClick={() => onSelectPatient(entry.patientCode)}
                          >
                            Xem hồ sơ
                          </Button>
                        </div>
                      </td>
                    )}
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
