import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';

import type {
  DoctorAssignedQueueEntryDto,
  DoctorCallNextResponseDto,
} from '../types/doctor-assigned-queue.types';
import { CALL_NEXT_SELECTION_REASON_META } from '../utils/doctor-assigned-queue-meta';

export interface DoctorCallNextBannerProps {
  result: DoctorCallNextResponseDto | null;
  error: string | null;
  onDismissResult: () => void;
  onDismissError: () => void;
  onOpenConsultation?: (visitCode: string) => void;
  onViewMedicalHistory?: (entry: DoctorAssignedQueueEntryDto) => void;
}

export function DoctorCallNextBanner({
  result,
  error,
  onDismissResult,
  onDismissError,
  onOpenConsultation,
  onViewMedicalHistory,
}: DoctorCallNextBannerProps) {
  if (!error && !result) {
    return null;
  }

  return (
    <div className="space-y-3">
      {error && (
        <div role="alert">
          <Alert tone="error">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="font-semibold">{error}</span>
              <button
                type="button"
                onClick={onDismissError}
                className="text-[11px] font-bold text-rose-700 hover:underline"
              >
                Đóng
              </button>
            </div>
          </Alert>
        </div>
      )}

      {result && (
        <div role="status">
          {result.outcome === 'CALLED' && (
            <Alert tone="success">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="space-y-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <Badge tone="emerald">Đã gọi bệnh nhân (CALLED)</Badge>
                    {result.selectionReason && (
                      <Badge tone={CALL_NEXT_SELECTION_REASON_META[result.selectionReason].tone}>
                        Lý do chọn: {CALL_NEXT_SELECTION_REASON_META[result.selectionReason].label}
                      </Badge>
                    )}
                  </div>
                  <div className="font-bold text-ink mt-1">{result.message}</div>
                  {result.calledEntry && (
                    <div className="text-[11px] text-emerald-900">
                      Số phiếu:{' '}
                      <span className="font-mono font-bold">{result.calledEntry.ticketNo}</span> (
                      <span className="font-mono">#{result.calledEntry.queueNumber}</span>) • Bệnh
                      nhân: <span className="font-semibold">{result.calledEntry.patientName}</span>{' '}
                      (<span className="font-mono">{result.calledEntry.patientCode}</span>) • Mã
                      lượt: <span className="font-mono">{result.calledEntry.visitCode}</span> •
                      Phòng: <span className="font-mono">{result.calledEntry.roomCode}</span> • Thứ
                      tự phục vụ:{' '}
                      <span className="font-mono font-bold">
                        #{result.calledEntry.servingOrder}
                      </span>
                      {result.selectionReason
                        ? ` — ${CALL_NEXT_SELECTION_REASON_META[result.selectionReason].description}`
                        : ''}
                    </div>
                  )}
                </div>
                <div className="flex items-center gap-2 shrink-0">
                  {result.calledEntry && onOpenConsultation && (
                    <button
                      type="button"
                      onClick={() => onOpenConsultation(result.calledEntry!.visitCode)}
                      className="rounded-lg bg-emerald-700 px-2.5 py-1 text-[11px] font-bold text-white transition-colors hover:bg-emerald-800"
                    >
                      Mở bàn khám lâm sàng
                    </button>
                  )}
                  {result.calledEntry && onViewMedicalHistory && (
                    <button
                      type="button"
                      onClick={() => onViewMedicalHistory(result.calledEntry!)}
                      className="rounded-lg border border-emerald-700 bg-white px-2.5 py-1 text-[11px] font-bold text-emerald-800 transition-colors hover:bg-emerald-100"
                    >
                      Xem lịch sử khám (UC32)
                    </button>
                  )}
                  <button
                    type="button"
                    onClick={onDismissResult}
                    className="text-[11px] font-bold text-emerald-800 hover:underline"
                  >
                    Đóng
                  </button>
                </div>
              </div>
            </Alert>
          )}

          {result.outcome === 'HOLD_FOR_SLOT' && (
            <Alert tone="warning">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="space-y-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <Badge tone="amber">Giữ khoảng trống bảo vệ lịch hẹn</Badge>
                  </div>
                  <div className="font-semibold text-amber-900">{result.message}</div>
                </div>
                <button
                  type="button"
                  onClick={onDismissResult}
                  className="text-[11px] font-bold text-amber-800 hover:underline shrink-0"
                >
                  Đóng
                </button>
              </div>
            </Alert>
          )}

          {result.outcome === 'NO_ELIGIBLE_PATIENT' && (
            <Alert tone="info">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="space-y-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <Badge tone="slate">Chưa có bệnh nhân đủ điều kiện</Badge>
                  </div>
                  <div className="font-semibold text-sky-900">{result.message}</div>
                </div>
                <button
                  type="button"
                  onClick={onDismissResult}
                  className="text-[11px] font-bold text-sky-800 hover:underline shrink-0"
                >
                  Đóng
                </button>
              </div>
            </Alert>
          )}
        </div>
      )}
    </div>
  );
}
