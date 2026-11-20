import { CheckCircle2 } from 'lucide-react';

import { Card } from '@/shared/ui/card';

import { formatPaidAt, formatVnd } from '../utils/payment-format';

interface PaymentPaidCardProps {
  amount: number | null;
  paidAt: string | null;
  reference: string | null;
}

export function PaymentPaidCard({ amount, paidAt, reference }: PaymentPaidCardProps) {
  return (
    <Card className="p-6">
      <div className="flex items-start gap-3">
        <CheckCircle2 className="mt-0.5 h-6 w-6 shrink-0 text-emerald-600" aria-hidden="true" />
        <div>
          <h2 className="text-lg font-extrabold text-ink">Đã thanh toán</h2>
          <p className="mt-1 text-sm text-sub">
            Hệ thống đã nhận được khoản chuyển khoản cho lượt khám này.
          </p>
        </div>
      </div>

      <dl className="mt-5 grid gap-3 rounded-xl border border-emerald-200 bg-emerald-50 p-4 sm:grid-cols-2">
        <div>
          <dt className="text-[11px] font-semibold uppercase tracking-wide text-emerald-800">
            Số tiền
          </dt>
          <dd className="mt-0.5 text-lg font-extrabold text-emerald-900">{formatVnd(amount)}</dd>
        </div>
        <div>
          <dt className="text-[11px] font-semibold uppercase tracking-wide text-emerald-800">
            Thời điểm
          </dt>
          <dd className="mt-0.5 text-sm font-semibold text-emerald-900">{formatPaidAt(paidAt)}</dd>
        </div>
        {reference ? (
          <div className="sm:col-span-2">
            <dt className="text-[11px] font-semibold uppercase tracking-wide text-emerald-800">
              Mã giao dịch ngân hàng
            </dt>
            <dd className="mt-0.5 truncate font-mono text-xs font-semibold text-emerald-900">
              {reference}
            </dd>
          </div>
        ) : null}
      </dl>
    </Card>
  );
}
