import { ArrowRight, CalendarClock, Hospital, Wallet } from 'lucide-react';
import { Link } from 'react-router-dom';

import { Badge } from '@/shared/ui/badge';
import { Card } from '@/shared/ui/card';

import type { PatientVisitListItemDto } from '../types/payment.types';
import { formatVisitTime } from '../utils/payment-format';

interface VisitCardProps {
  visit: PatientVisitListItemDto;
}

const CLOSED_VISIT_STATUSES = ['CANCELLED', 'NO_SHOW'];

function visitStatusMeta(status: string | null): {
  label: string;
  tone: 'sky' | 'emerald' | 'amber' | 'rose' | 'slate';
} {
  switch (status?.toUpperCase()) {
    case 'CHECKED_IN':
      return { label: 'Đã check-in', tone: 'sky' };
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
  const visitStatus = visitStatusMeta(visit.visitStatus);
  const paymentStatus = paymentStatusMeta(visit.paymentStatus);
  const isClosed = CLOSED_VISIT_STATUSES.includes(visit.visitStatus?.toUpperCase() ?? '');
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
