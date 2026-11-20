import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { formatVnd, settlementLabel } from '../lib/payment-format';

interface PaymentSummaryCardProps {
  settlementStatus: string;
  payableTotal: number;
  recognizedPaidTotal: number;
  amountDue: number | null;
}

const SETTLEMENT_TONES: Record<string, 'sky' | 'emerald' | 'amber' | 'rose' | 'slate'> = {
  UNPAID: 'amber',
  PAID: 'emerald',
  MISMATCH: 'rose',
  UNDETERMINED: 'slate',
};

export function PaymentSummaryCard({
  settlementStatus,
  payableTotal,
  recognizedPaidTotal,
  amountDue,
}: PaymentSummaryCardProps) {
  return (
    <Card>
      <CardHeader className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-sm font-bold text-ink">Tổng hợp viện phí</h2>
        <Badge tone={SETTLEMENT_TONES[settlementStatus] ?? 'slate'}>
          {settlementLabel(settlementStatus)}
        </Badge>
      </CardHeader>
      <CardContent className="grid gap-4 sm:grid-cols-3">
        <div>
          <p className="text-xs text-sub">Tổng phí phải thu</p>
          <p className="mt-1 text-lg font-bold text-ink">{formatVnd(payableTotal)}</p>
        </div>
        <div>
          <p className="text-xs text-sub">Đã ghi nhận thanh toán</p>
          <p className="mt-1 text-lg font-bold text-ink">{formatVnd(recognizedPaidTotal)}</p>
        </div>
        <div>
          <p className="text-xs text-sub">Còn phải trả</p>
          <p className="mt-1 text-lg font-bold text-primary">{formatVnd(amountDue)}</p>
          {amountDue === null ? (
            <p className="mt-1 text-[11px] text-sub">
              Chưa xác định được số tiền cần thu cho lượt khám này.
            </p>
          ) : null}
        </div>
      </CardContent>
    </Card>
  );
}
