import { Check, Clock, Copy, ScanLine } from 'lucide-react';
import { QRCodeSVG } from 'qrcode.react';
import { useState } from 'react';

import { Badge } from '@/shared/ui/badge';
import { Card } from '@/shared/ui/card';
import { toast } from '@/shared/ui/toast';

import type { QrPaymentResponseDto } from '../types/payment.types';
import { formatCountdown, formatVnd } from '../utils/payment-format';

interface PaymentQrCardProps {
  qr: QrPaymentResponseDto;
  remainingMs: number;
  isExpired: boolean;
}

interface CopyRowProps {
  label: string;
  value: string;
  tone?: 'default' | 'code';
}

function CopyRow({ label, value, tone = 'default' }: CopyRowProps) {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      toast.success(`Đã sao chép ${label.toLowerCase()}.`);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      toast.error('Trình duyệt không cho phép sao chép. Vui lòng chọn và sao chép thủ công.');
    }
  };

  return (
    <div className="flex items-center justify-between gap-3 py-2">
      <dt className="text-xs text-sub">{label}</dt>
      <dd className="flex min-w-0 items-center gap-2">
        <span
          className={`truncate text-sm font-semibold text-ink ${tone === 'code' ? 'font-mono' : ''}`}
        >
          {value}
        </span>
        <button
          type="button"
          onClick={() => void handleCopy()}
          aria-label={`Sao chép ${label.toLowerCase()}`}
          className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-border text-sub transition-colors hover:bg-canvas hover:text-primary focus-visible:ring-2 focus-visible:ring-primary"
        >
          {copied ? (
            <Check className="h-4 w-4 text-emerald-600" aria-hidden="true" />
          ) : (
            <Copy className="h-4 w-4" aria-hidden="true" />
          )}
        </button>
      </dd>
    </div>
  );
}

export function PaymentQrCard({ qr, remainingMs, isExpired }: PaymentQrCardProps) {
  const normalizedStatus = qr.status?.trim().toUpperCase() ?? '';
  const hasPayload = Boolean(qr.qrPayload?.trim());
  const canRenderQrImage = normalizedStatus === 'PENDING' && !isExpired && hasPayload;

  return (
    <Card className="overflow-hidden">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border px-6 py-4">
        <div>
          <h2 className="text-sm font-bold text-ink">Quét mã để chuyển khoản</h2>
          <p className="mt-0.5 text-xs text-sub">
            Mở app ngân hàng, chọn quét QR và kiểm tra đúng số tiền, nội dung.
          </p>
        </div>
        {normalizedStatus === 'PAID' ? (
          <Badge tone="emerald">ĐÃ THANH TOÁN</Badge>
        ) : isExpired || normalizedStatus !== 'PENDING' ? (
          <Badge tone="slate">MÃ ĐÃ ĐÓNG / HẾT HẠN</Badge>
        ) : (
          <Badge tone="amber">CHỜ THANH TOÁN</Badge>
        )}
      </div>

      <div className="grid gap-6 px-6 py-6 sm:grid-cols-[208px_1fr] sm:items-start">
        <div className="mx-auto flex flex-col items-center gap-2">
          {canRenderQrImage ? (
            <div
              role="img"
              aria-label={`Mã QR thanh toán ${qr.paymentCode}`}
              className="rounded-2xl border border-border bg-white p-3"
            >
              <QRCodeSVG value={qr.qrPayload} size={184} level="M" marginSize={0} />
            </div>
          ) : (
            <div className="flex h-[210px] w-[210px] flex-col items-center justify-center rounded-2xl border border-dashed border-border bg-canvas p-4 text-center text-xs text-sub">
              <Clock className="mb-2 h-6 w-6 text-sub" aria-hidden="true" />
              <span className="font-semibold text-navy">
                {normalizedStatus === 'PAID'
                  ? 'Giao dịch này đã được thanh toán'
                  : 'Mã QR không còn hiệu lực quét'}
              </span>
              <span className="mt-1 text-[11px]">
                {normalizedStatus === 'PAID'
                  ? 'Không hiển thị lại mã QR cho giao dịch đã hoàn tất.'
                  : 'Vui lòng tạo mã QR mới để tiếp tục thanh toán.'}
              </span>
            </div>
          )}
          <p className="flex items-center gap-1.5 text-xs font-semibold text-sub">
            <ScanLine className="h-4 w-4 text-primary" aria-hidden="true" />
            <span>VietQR • {qr.bankBin}</span>
          </p>
        </div>

        <div>
          <div className="rounded-xl border border-border bg-canvas px-4 py-3">
            <p className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Số tiền cần thanh toán
            </p>
            <p className="mt-0.5 text-2xl font-extrabold text-ink">{formatVnd(qr.amount)}</p>
          </div>

          <dl className="mt-3 divide-y divide-border">
            <CopyRow label="Chủ tài khoản" value={qr.accountName} />
            <CopyRow label="Số tài khoản" value={qr.accountNumber} tone="code" />
            <CopyRow label="Nội dung chuyển khoản" value={qr.transferContent} tone="code" />
          </dl>

          {qr.allocations && qr.allocations.length > 1 ? (
            <div className="mt-3 rounded-xl border border-border bg-canvas px-3.5 py-2.5 text-xs">
              <p className="font-semibold text-navy">
                Chi tiết gộp ({qr.allocations.length} dịch vụ)
              </p>
              <ul className="mt-1.5 space-y-1">
                {qr.allocations.map((alloc) => (
                  <li
                    key={alloc.visitChargeId}
                    className="flex items-center justify-between gap-2 text-sub"
                  >
                    <span className="truncate">
                      {alloc.serviceName ?? `#${alloc.visitChargeId}`}
                    </span>
                    <span className="shrink-0 font-mono font-semibold text-ink">
                      {formatVnd(alloc.allocatedAmount)}
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          <p
            className={`mt-3 flex items-center gap-1.5 text-xs font-semibold ${
              isExpired ? 'text-danger' : 'text-emerald-700'
            }`}
            aria-live="polite"
          >
            <Clock className="h-4 w-4" aria-hidden="true" />
            {isExpired
              ? 'Mã QR đã hết hiệu lực. Tạo mã mới để tiếp tục.'
              : `Mã hết hạn sau ${formatCountdown(remainingMs)}`}
          </p>
        </div>
      </div>
    </Card>
  );
}
