import { ArrowRight, CalendarClock, CheckCircle2, Hospital, Wallet } from 'lucide-react';
import { Link } from 'react-router-dom';

import { Badge } from '@/shared/ui/badge';
import { Card } from '@/shared/ui/card';

import type { PatientVisitListItemDto } from '../types/payment.types';
import { formatVisitTime } from '../utils/payment-format';

interface VisitCardProps {
  visit: PatientVisitListItemDto;
}

const CLOSED_VISIT_STATUSES = ['CANCELLED', 'NO_SHOW'];

function visitStatusMeta(
  status: string | null,
  readyForReturnAt?: string | null,
  isPresentToday?: boolean,
): {
  label: string;
  tone: 'sky' | 'emerald' | 'amber' | 'rose' | 'slate';
} {
  const upper = status?.toUpperCase();
  if (
    Boolean(readyForReturnAt) &&
    !isPresentToday &&
    upper !== 'COMPLETED' &&
    upper !== 'CANCELLED' &&
    upper !== 'NO_SHOW'
  ) {
    return { label: 'Đã có kết quả CLS', tone: 'emerald' };
  }

  switch (upper) {
    case 'CHECKED_IN':
    case 'WAITING':
      return { label: 'Đã check-in', tone: 'sky' };
    case 'AWAITING_DIAGNOSTIC':
    case 'AWAITING_RESULTS':
      return { label: 'Chờ kết quả CLS', tone: 'amber' };
    case 'RETURN_CONSULTATION':
      return {
        label: isPresentToday ? 'Chờ khám kết luận' : 'Đã có kết quả CLS',
        tone: 'emerald',
      };
    case 'IN_PROGRESS':
      return { label: 'Đang khám', tone: 'sky' };
    case 'COMPLETED':
      return { label: 'Đã hoàn tất', tone: 'emerald' };
    case 'CANCELLED':
      return { label: 'Đã hủy', tone: 'rose' };
    case 'NO_SHOW':
      return { label: 'Không đến', tone: 'rose' };
    default:
      return { label: status ?? 'Chưa rõ', tone: 'slate' };
  }
}

function paymentStatusMeta(status: string | null): {
  label: string;
  tone: 'sky' | 'emerald' | 'amber' | 'rose' | 'slate';
} {
  switch (status?.toUpperCase()) {
    case 'PAID':
      return { label: 'Đã thanh toán', tone: 'emerald' };
    case 'PENDING':
      return { label: 'Chờ thanh toán', tone: 'amber' };
    case 'EXPIRED':
      return { label: 'Mã QR hết hạn', tone: 'slate' };
    case 'FAILED':
      return { label: 'Thanh toán thất bại', tone: 'rose' };
    case 'CANCELLED':
      return { label: 'Giao dịch đã hủy', tone: 'rose' };
    default:
      return { label: 'Chưa thanh toán', tone: 'slate' };
  }
}

export function VisitCard({ visit }: VisitCardProps) {
  const visitStatus = visitStatusMeta(
    visit.visitStatus,
    visit.readyForReturnAt,
    visit.isPresentToday,
  );
  const paymentStatus = paymentStatusMeta(visit.paymentStatus);
  const upperVisitStatus = visit.visitStatus?.toUpperCase() ?? '';
  const isClosed = CLOSED_VISIT_STATUSES.includes(upperVisitStatus);
  const isCompleted = upperVisitStatus === 'COMPLETED' || upperVisitStatus === 'DONE';
  const showReturnReadyNotice =
    Boolean(visit.readyForReturnAt) && !visit.isPresentToday && !isClosed && !isCompleted;
  const isPaid = visit.paymentStatus?.toUpperCase() === 'PAID';
  const target = `/payments/visits/${encodeURIComponent(visit.visitCode ?? String(visit.visitId))}`;

  return (
    <Card className="p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="font-mono text-sm font-bold text-ink">
            #{visit.visitCode ?? visit.visitId}
          </p>
          <p className="mt-1 flex items-center gap-1.5 text-xs text-sub">
            <CalendarClock className="h-3.5 w-3.5" aria-hidden="true" />
            <span>{formatVisitTime(visit.checkedInAt)}</span>
          </p>
          {visit.hospitalName ? (
            <p className="mt-1 flex items-center gap-1.5 text-xs text-sub">
              <Hospital className="h-3.5 w-3.5" aria-hidden="true" />
              <span className="truncate">{visit.hospitalName}</span>
            </p>
          ) : null}
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Badge tone={visitStatus.tone}>{visitStatus.label}</Badge>
          <Badge tone={paymentStatus.tone}>{paymentStatus.label}</Badge>
        </div>
      </div>

      {showReturnReadyNotice ? (
        <div
          role="status"
          className="mt-3 flex items-start gap-2 rounded-xl border border-emerald-200 bg-emerald-50/80 px-3.5 py-2.5 text-xs font-semibold text-emerald-800"
        >
          <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" aria-hidden="true" />
          <span>
            Đủ điều kiện quay lại khám kết luận — vui lòng đến quầy Tiếp đón xác nhận có mặt
          </span>
        </div>
      ) : null}

      <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4">
        <p className="text-xs text-sub">
          {isClosed
            ? 'Lượt khám đã đóng, không thể thanh toán.'
            : isPaid
              ? 'Khoản phí của lượt khám này đã được ghi nhận.'
              : 'Xem chi tiết phí và tạo mã VietQR nếu cần.'}
        </p>

        {isClosed ? (
          <span className="inline-flex items-center gap-2 rounded-xl bg-slate-100 px-4 py-2 text-sm font-semibold text-slate-500">
            Đã đóng
          </span>
        ) : (
          <Link
            to={target}
            className="inline-flex items-center gap-2 rounded-xl bg-primary px-4 py-2 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-primary-dark focus-visible:ring-2 focus-visible:ring-primary"
          >
            <Wallet className="h-4 w-4" aria-hidden="true" />
            <span>{isPaid ? 'Xem biên nhận' : 'Thanh toán viện phí'}</span>
            <ArrowRight className="h-4 w-4" aria-hidden="true" />
          </Link>
        )}
      </div>
    </Card>
  );
}
