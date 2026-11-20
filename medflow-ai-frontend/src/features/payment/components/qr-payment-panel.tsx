import { CheckCircle2, Clock } from 'lucide-react';
import { QRCodeSVG } from 'qrcode.react';

import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';
import { Spinner } from '@/shared/ui/spinner';
import { toast } from '@/shared/ui/toast';

import { formatVnd, paymentLabel } from '../lib/payment-format';

interface QrPaymentPanelProps {
  paymentCode: string;
  amount: number | null;
  status: string | null;
  qrPayload: string | null;
  bankBin?: string | null;
  accountNumber?: string | null;
  accountName?: string | null;
  transferContent: string;
  expiresAtLabel?: string | null;
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-start justify-between gap-4 border-b border-border py-2 text-xs last:border-b-0">
      <span className="text-sub">{label}</span>
      <span className="text-right font-semibold text-ink">{value}</span>
    </div>
  );
}

export function QrPaymentPanel({
  paymentCode,
  amount,
  status,
  qrPayload,
  bankBin,
  accountNumber,
  accountName,
  transferContent,
  expiresAtLabel,
}: QrPaymentPanelProps) {
  const isPending = status === 'PENDING';
  const isPaid = status === 'PAID';

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(transferContent);
      toast.success('Đã sao chép nội dung chuyển khoản.');
    } catch {
      toast.error('Không sao chép được. Vui lòng ghi lại nội dung bằng tay.');
    }
  };

  return (
    <Card>
      <CardHeader className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-sm font-bold text-ink">Thanh toán bằng VietQR</h2>
        <Badge tone={isPaid ? 'emerald' : isPending ? 'amber' : 'rose'}>
          {paymentLabel(status)}
        </Badge>
      </CardHeader>
      <CardContent className="space-y-4">
        {isPaid ? (
          <Alert tone="success">
            <span className="font-semibold">Đã thanh toán {formatVnd(amount)}.</span> Hệ thống đã
            ghi nhận giao dịch chuyển khoản cho mã {paymentCode}.
          </Alert>
        ) : null}

        {isPending ? (
          <div className="flex items-center gap-2 text-xs font-semibold text-amber-700">
            <Spinner className="h-4 w-4" />
            Đang chờ thanh toán...
            <Clock className="h-3.5 w-3.5" aria-hidden="true" />
            {expiresAtLabel ? (
              <span className="font-normal text-sub">Hết hạn {expiresAtLabel}</span>
            ) : null}
          </div>
        ) : null}

        {qrPayload ? (
          <div className="flex justify-center">
            <div
              role="img"
              aria-label={`Mã VietQR ${paymentCode}`}
              className="rounded-xl border border-border bg-white p-3"
            >
              <QRCodeSVG value={qrPayload} size={220} level="M" />
            </div>
          </div>
        ) : (
          <Alert tone="warning">
            Chưa tải được ảnh QR cho mã này. Dùng số tài khoản và nội dung bên dưới để chuyển khoản.
          </Alert>
        )}

        <div>
          {bankBin ? <InfoRow label="Ngân hàng (BIN)" value={bankBin} /> : null}
          {accountNumber ? <InfoRow label="Số tài khoản" value={accountNumber} /> : null}
          {accountName ? <InfoRow label="Chủ tài khoản" value={accountName} /> : null}
          <InfoRow label="Số tiền" value={formatVnd(amount)} />
          <div className="flex items-start justify-between gap-4 py-2 text-xs">
            <span className="text-sub">Nội dung chuyển khoản</span>
            <span className="flex items-center gap-2">
              <span className="font-mono font-bold text-ink">{transferContent}</span>
              <button
                type="button"
                onClick={handleCopy}
                className="rounded-md border border-border px-2 py-0.5 text-[11px] font-semibold text-sub hover:bg-canvas"
              >
                Sao chép
              </button>
            </span>
          </div>
        </div>

        {!isPaid ? (
          <p className="text-xs leading-relaxed text-sub">
            Mở ứng dụng ngân hàng và quét mã, hoặc chuyển khoản đúng nội dung bên trên. Hệ thống tự
            xác nhận khi tiền vào tài khoản; không cần tải lại trang.
          </p>
        ) : (
          <p className="flex items-center gap-1.5 text-xs font-semibold text-emerald-700">
            <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
            Giao dịch đã được đối soát.
          </p>
        )}
      </CardContent>
    </Card>
  );
}
