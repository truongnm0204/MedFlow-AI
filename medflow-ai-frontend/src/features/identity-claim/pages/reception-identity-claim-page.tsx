import { useState } from 'react';
import { ArrowLeft, CheckCircle2, FileWarning, ShieldCheck, XCircle } from 'lucide-react';
import { useNavigate } from 'react-router-dom';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card } from '@/shared/ui/card';
import { Modal } from '@/shared/ui/modal';
import { toast } from '@/shared/ui/toast';

import { ReceptionSidebar } from '@/features/reception/components/reception-sidebar';

import {
  useApproveIdentityClaimMutation,
  useIdentityClaimQueueQuery,
  useRejectIdentityClaimMutation,
} from '../hooks/use-identity-claims';
import type { StaffIdentityClaim } from '../types/identity-claim.types';

type QueueTab = 'PENDING' | 'NEEDS_RECONCILIATION';

const TAB_LABELS: Record<QueueTab, string> = {
  PENDING: 'Chờ xác minh',
  NEEDS_RECONCILIATION: 'Cần đối soát',
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

export function ReceptionIdentityClaimPage() {
  const navigate = useNavigate();
  const [tab, setTab] = useState<QueueTab>('PENDING');
  const [rejecting, setRejecting] = useState<StaffIdentityClaim | null>(null);
  const [reason, setReason] = useState('');

  const queueQuery = useIdentityClaimQueueQuery(tab);
  const approve = useApproveIdentityClaimMutation();
  const reject = useRejectIdentityClaimMutation();

  const handleApprove = async (claim: StaffIdentityClaim) => {
    try {
      const result = await approve.mutateAsync(claim.claimId);
      toast.success(
        result.status === 'NEEDS_RECONCILIATION'
          ? 'Hồ sơ cần đối soát trước khi liên kết.'
          : 'Đã liên kết hồ sơ.',
      );
    } catch {
      // lỗi hiển thị qua approve.error
    }
  };

  const handleReject = async () => {
    if (!rejecting || reason.trim().length < 5) return;
    try {
      await reject.mutateAsync({ claimId: rejecting.claimId, reason: reason.trim() });
      toast.success('Đã từ chối yêu cầu.');
      setRejecting(null);
      setReason('');
    } catch {
      // lỗi hiển thị qua reject.error
    }
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <ReceptionSidebar
        activeItem="identity-claims"
        onItemSelect={(item) => {
          if (item === 'dashboard') navigate('/reception');
          if (item === 'patients') navigate('/reception/patients');
          if (item === 'queue') navigate('/reception/queue');
          if (item === 'identity-claims') navigate('/reception/identity-claims');
        }}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 items-center justify-between border-b border-border bg-white px-6">
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={() => navigate('/reception')}
              aria-label="Quay lại dashboard tiếp đón"
              className="rounded-xl p-2 text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-800"
            >
              <ArrowLeft className="h-5 w-5" aria-hidden="true" />
            </button>
            <div>
              <div className="text-sm font-bold text-slate-900">Xác minh liên kết hồ sơ</div>
              <div className="text-[11px] text-slate-500">
                Đối chiếu giấy tờ trước khi phê duyệt
              </div>
            </div>
          </div>
          <ShieldCheck className="h-5 w-5 text-primary" aria-hidden="true" />
        </header>

        <main className="mx-auto w-full max-w-5xl flex-1 space-y-5 p-6">
          <div className="flex flex-wrap gap-2" role="tablist" aria-label="Trạng thái yêu cầu">
            {(Object.keys(TAB_LABELS) as QueueTab[]).map((key) => (
              <button
                key={key}
                type="button"
                role="tab"
                aria-selected={tab === key}
                onClick={() => setTab(key)}
                className={`rounded-full border px-4 py-1.5 text-xs font-semibold transition-colors ${
                  tab === key
                    ? 'border-primary bg-primary text-white'
                    : 'border-border bg-white text-sub hover:bg-canvas'
                }`}
              >
                {TAB_LABELS[key]}
              </button>
            ))}
          </div>

          {approve.isError ? <Alert tone="error">{getErrorMessage(approve.error)}</Alert> : null}
          {reject.isError ? <Alert tone="error">{getErrorMessage(reject.error)}</Alert> : null}
          {queueQuery.isError ? (
            <Alert tone="error">{getErrorMessage(queueQuery.error)}</Alert>
          ) : null}

          {queueQuery.isPending ? (
            <Card className="p-8 text-center text-sm text-sub">Đang tải hàng đợi…</Card>
          ) : (queueQuery.data ?? []).length === 0 ? (
            <Card className="flex flex-col items-center gap-2 p-10 text-center">
              <CheckCircle2 className="h-7 w-7 text-emerald-600" aria-hidden="true" />
              <p className="text-sm font-semibold text-ink">Không có yêu cầu nào</p>
              <p className="text-xs text-sub">
                Hàng đợi {TAB_LABELS[tab].toLowerCase()} đang trống.
              </p>
            </Card>
          ) : (
            <div className="space-y-3">
              {queueQuery.data!.map((claim) => (
                <Card key={claim.claimId} className="p-5">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div>
                      <p className="font-mono text-sm font-bold text-ink">
                        Hồ sơ: {claim.requestedPatientCode}
                      </p>
                      <p className="mt-1 text-xs text-sub">
                        Yêu cầu #{claim.claimId} • gửi {formatDateTime(claim.createdAt)}
                      </p>
                      <p className="mt-0.5 text-xs text-sub">
                        Hết hạn {formatDateTime(claim.expiresAt)} • cơ sở #{claim.hospitalId}
                      </p>
                    </div>
                    <Badge tone={claim.status === 'PENDING' ? 'amber' : 'sky'}>
                      {TAB_LABELS[claim.status as QueueTab] ?? claim.status}
                    </Badge>
                  </div>

                  {claim.status === 'PENDING' ? (
                    <div className="mt-4 flex flex-wrap gap-2 border-t border-border pt-4">
                      <Button
                        type="button"
                        variant="primary"
                        size="md"
                        loading={approve.isPending}
                        onClick={() => void handleApprove(claim)}
                      >
                        <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
                        Phê duyệt
                      </Button>
                      <Button
                        type="button"
                        variant="secondary"
                        size="md"
                        onClick={() => {
                          setRejecting(claim);
                          setReason('');
                        }}
                      >
                        <XCircle className="h-4 w-4" aria-hidden="true" />
                        Từ chối
                      </Button>
                    </div>
                  ) : (
                    <p className="mt-4 flex items-center gap-1.5 border-t border-border pt-4 text-xs text-amber-700">
                      <FileWarning className="h-4 w-4" aria-hidden="true" />
                      Hồ sơ cần bộ phận quản lý hồ sơ đối soát trước khi liên kết.
                    </p>
                  )}
                </Card>
              ))}
            </div>
          )}
        </main>
      </div>

      <Modal open={rejecting !== null} onClose={() => setRejecting(null)} maxWidth="max-w-md">
        <h2 className="text-sm font-bold text-ink">Từ chối yêu cầu #{rejecting?.claimId}</h2>
        <p className="mt-1 text-xs text-sub">
          Lý do sẽ được gửi tới bệnh nhân. Không nhập thông tin CCCD hay dữ liệu bệnh án.
        </p>
        <label className="mt-4 block">
          <span className="mb-1 block text-xs font-semibold text-ink">Lý do từ chối</span>
          <textarea
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            rows={4}
            maxLength={500}
            className="w-full rounded-lg border border-border px-3 py-2 text-sm focus-visible:ring-2 focus-visible:ring-primary"
            placeholder="Tối thiểu 5 ký tự"
          />
        </label>
        <div className="mt-4 flex justify-end gap-2">
          <Button type="button" variant="secondary" size="md" onClick={() => setRejecting(null)}>
            Hủy
          </Button>
          <Button
            type="button"
            variant="danger"
            size="md"
            loading={reject.isPending}
            disabled={reason.trim().length < 5}
            onClick={() => void handleReject()}
          >
            Từ chối
          </Button>
        </div>
      </Modal>
    </div>
  );
}
