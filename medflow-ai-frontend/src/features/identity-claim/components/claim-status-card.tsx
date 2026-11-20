import { CheckCircle2, Clock, Info, TriangleAlert } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Card } from '@/shared/ui/card';

import type { IdentityClaim } from '../types/identity-claim.types';

interface ClaimStatusCardProps {
  claim: IdentityClaim;
  onStartNew?: () => void;
}

const META: Record<
  string,
  { label: string; tone: 'sky' | 'emerald' | 'amber' | 'rose' | 'slate'; icon: typeof Info }
> = {
  PENDING: { label: 'Đang chờ xác minh', tone: 'amber', icon: Clock },
  APPROVED: { label: 'Đã liên kết hồ sơ', tone: 'emerald', icon: CheckCircle2 },
  NEEDS_RECONCILIATION: { label: 'Đang đối soát hồ sơ', tone: 'amber', icon: TriangleAlert },
  REJECTED: { label: 'Yêu cầu bị từ chối', tone: 'rose', icon: TriangleAlert },
  EXPIRED: { label: 'Yêu cầu đã hết hạn', tone: 'slate', icon: Clock },
  CANCELLED: { label: 'Yêu cầu đã hủy', tone: 'slate', icon: Info },
};

function formatDateTime(value: string): string {
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return parsed.toLocaleString('vi-VN', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function ClaimStatusCard({ claim, onStartNew }: ClaimStatusCardProps) {
  const meta = META[claim.status] ?? {
    label: claim.status,
    tone: 'slate' as const,
    icon: Info,
  };
  const Icon = meta.icon;
  const isTerminal = ['APPROVED', 'REJECTED', 'EXPIRED', 'CANCELLED'].includes(claim.status);

  return (
    <Card className="p-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex items-start gap-3">
          <Icon
            className={`mt-0.5 h-5 w-5 shrink-0 ${
              claim.status === 'APPROVED'
                ? 'text-emerald-600'
                : claim.status === 'REJECTED'
                  ? 'text-rose-600'
                  : 'text-primary'
            }`}
            aria-hidden="true"
          />
          <div>
            <p className="font-mono text-sm font-bold text-ink">Yêu cầu #{claim.claimId}</p>
            <p className="mt-0.5 text-xs text-sub">Gửi lúc {formatDateTime(claim.createdAt)}</p>
          </div>
        </div>
        <Badge tone={meta.tone}>{meta.label}</Badge>
      </div>

      <div className="mt-4 rounded-xl border border-border bg-canvas p-4 text-sm text-sub">
        {claim.status === 'PENDING' ? (
          <p>
            Hồ sơ của bạn đang chờ nhân viên có thẩm quyền xác minh tại cơ sở đã chọn. Bạn không cần
            gửi lại yêu cầu; trạng thái tự cập nhật.
          </p>
        ) : null}
        {claim.status === 'APPROVED' ? (
          <p>
            Tài khoản đã được liên kết với hồ sơ bệnh nhân gốc. Lịch sử khám và viện phí sẽ hiển thị
            theo hồ sơ này.
          </p>
        ) : null}
        {claim.status === 'NEEDS_RECONCILIATION' ? (
          <p>
            Hồ sơ hiện tại còn dữ liệu khám nên cần bộ phận quản lý hồ sơ đối soát trước khi liên
            kết. Vui lòng liên hệ quầy tiếp nhận để được hỗ trợ.
          </p>
        ) : null}
        {claim.status === 'REJECTED' ? (
          <p>
            Yêu cầu chưa được chấp nhận.
            {claim.decisionNote ? ` Lý do: ${claim.decisionNote}` : ''}
          </p>
        ) : null}
        {claim.status === 'EXPIRED' || claim.status === 'CANCELLED' ? (
          <p>Yêu cầu không còn hiệu lực. Bạn có thể gửi yêu cầu mới.</p>
        ) : null}
        {!isTerminal && claim.status !== 'NEEDS_RECONCILIATION' ? (
          <p className="mt-2 text-xs">Hết hạn: {formatDateTime(claim.expiresAt)}.</p>
        ) : null}
      </div>

      {isTerminal && onStartNew ? (
        <button
          type="button"
          onClick={onStartNew}
          className="mt-4 text-xs font-semibold text-primary transition-colors hover:text-primary-dark"
        >
          Gửi yêu cầu mới
        </button>
      ) : null}
    </Card>
  );
}
