import { Banknote, CheckCircle2, RefreshCw, XCircle } from 'lucide-react';
import { useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import type {
  CollectCashPaymentResponse,
  VisitChargeLine,
} from '@/features/payment/types/payment.types';
import {
  formatPaidAt,
  formatVnd,
  lineSettlementLabel,
} from '@/features/payment/utils/payment-format';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';
import { Spinner } from '@/shared/ui/spinner';

import {
  collectReceptionCashPaymentApi,
  declineReceptionDiagnosticOrderApi,
  fetchReceptionVisitChargesApi,
} from '../api/reception-cash-payment.api';

export interface ReceptionCashCollectionTarget {
  visitIdOrCode: string;
  patientName: string;
  patientCode?: string;
}

export interface ReceptionCashCollectionModalProps {
  open: boolean;
  target: ReceptionCashCollectionTarget | null;
  onClose: () => void;
  onCollected?: (receipt: CollectCashPaymentResponse) => void;
}

function generateIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `cash-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

export function ReceptionCashCollectionModal({
  open,
  target,
  onClose,
  onCollected,
}: ReceptionCashCollectionModalProps) {
  const queryClient = useQueryClient();
  const visitIdOrCode = target?.visitIdOrCode?.trim() ?? '';

  const [selectedById, setSelectedById] = useState<Record<number, boolean>>({});
  const [notes, setNotes] = useState('');
  const [receipt, setReceipt] = useState<CollectCashPaymentResponse | null>(null);
  const [skipConfirmOrderId, setSkipConfirmOrderId] = useState<number | null>(null);
  const [skipReason, setSkipReason] = useState('');

  // Keep stable idempotencyKey per sorted charge selection so retries never double-collect
  const idempotencyKeysRef = useRef<Record<string, string>>({});

  const chargesQuery = useQuery({
    queryKey: ['reception-visit-charges', visitIdOrCode],
    queryFn: () => fetchReceptionVisitChargesApi(visitIdOrCode),
    enabled: open && Boolean(visitIdOrCode),
  });

  const collectMutation = useMutation({
    mutationFn: ({
      chargeIds,
      idempotencyKey,
      noteText,
    }: {
      chargeIds: number[];
      idempotencyKey: string;
      noteText?: string | null;
    }) =>
      collectReceptionCashPaymentApi(visitIdOrCode, {
        visitChargeIds: chargeIds,
        idempotencyKey,
        notes: noteText,
      }),
    onSuccess: (res) => {
      setReceipt(res);
      void queryClient.invalidateQueries({ queryKey: ['reception-visit-charges', visitIdOrCode] });
      onCollected?.(res);
    },
  });

  const declineMutation = useMutation({
    mutationFn: ({
      orderId,
      confirmAbandonPaid,
      reason,
    }: {
      orderId: number;
      confirmAbandonPaid?: boolean;
      reason: string;
    }) =>
      declineReceptionDiagnosticOrderApi(visitIdOrCode, orderId, {
        confirmAbandonPaid,
        reason,
      }),
    onSuccess: () => {
      setSkipConfirmOrderId(null);
      setSkipReason('');
      void queryClient.invalidateQueries({ queryKey: ['reception-visit-charges', visitIdOrCode] });
    },
  });

  const charges = chargesQuery.data;

  const selectedIds = useMemo<number[]>(() => {
    if (!charges) return [];
    return charges.charges
      .filter((line: VisitChargeLine) => {
        if (line.chargeId == null || line.lineSettlementStatus !== 'UNPAID') return false;
        if ((line.unitPrice ?? 0) <= 0) return false;
        const sel = line.selectionStatus?.toUpperCase();
        if (sel === 'DECLINED' || sel === 'SKIPPED_AFTER_PAID' || sel === 'REVIEW_REQUIRED') {
          return false;
        }
        const manual = selectedById[line.chargeId];
        if (manual !== undefined) return manual;
        return sel !== 'UNDECIDED';
      })
      .map((l) => l.chargeId!)
      .sort((a, b) => a - b);
  }, [charges, selectedById]);

  const selectedTotal = useMemo(() => {
    if (!charges) return 0;
    const set = new Set(selectedIds);
    return charges.charges
      .filter((l) => l.chargeId != null && set.has(l.chargeId))
      .reduce((sum, l) => sum + (l.unitPrice ?? 0), 0);
  }, [charges, selectedIds]);

  const handleCollectCash = async () => {
    if (selectedIds.length === 0) return;
    const selectionKey = `${visitIdOrCode}:${selectedIds.join(',')}`;
    if (!idempotencyKeysRef.current[selectionKey]) {
      idempotencyKeysRef.current[selectionKey] = generateIdempotencyKey();
    }
    const idempotencyKey = idempotencyKeysRef.current[selectionKey];

    try {
      await collectMutation.mutateAsync({
        chargeIds: selectedIds,
        idempotencyKey,
        noteText: notes.trim() || null,
      });
    } catch {
      /* Error rendered via collectMutation.error */
    }
  };

  if (!target) return null;

  const activeError = collectMutation.error ?? declineMutation.error;

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-2xl">
      <div className="p-6 space-y-4">
        <div className="flex items-start justify-between gap-3 pr-8">
          <div className="flex items-center gap-2.5">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-emerald-50 text-emerald-700 border border-emerald-200">
              <Banknote className="h-5 w-5" aria-hidden="true" />
            </div>
            <div>
              <h2 className="text-base font-bold text-navy">
                Thu tiền mặt tại quầy tiếp nhận (PAYMENT_COLLECT)
              </h2>
              <p className="text-xs text-sub">
                Bệnh nhân: <strong className="text-ink">{target.patientName}</strong>
                {target.patientCode ? ` (${target.patientCode})` : ''} • Mã ca:{' '}
                <span className="font-mono font-semibold text-navy">{visitIdOrCode}</span>
              </p>
            </div>
          </div>
        </div>

        {chargesQuery.isPending ? (
          <div className="flex items-center justify-center py-8">
            <Spinner />
          </div>
        ) : chargesQuery.isError ? (
          <Alert tone="error">{getErrorMessage(chargesQuery.error)}</Alert>
        ) : charges ? (
          <div className="space-y-4">
            {activeError ? <Alert tone="error">{getErrorMessage(activeError)}</Alert> : null}

            {receipt ? (
              <div className="rounded-2xl border border-emerald-200 bg-emerald-50/70 p-4 space-y-3 text-xs">
                <div className="flex items-center justify-between gap-2">
                  <div className="flex items-center gap-2 font-bold text-emerald-900 text-sm">
                    <CheckCircle2 className="h-4 w-4 text-emerald-700" aria-hidden="true" />
                    <span>Đã thu tiền mặt thành công</span>
                  </div>
                  <Badge tone="emerald">{receipt.paymentCode}</Badge>
                </div>

                <div className="grid grid-cols-2 gap-2 rounded-xl bg-white border border-border p-3">
                  <div>
                    <span className="text-sub">Số tiền thực thu:</span>
                    <div className="font-mono text-base font-extrabold text-ink">
                      {formatVnd(receipt.amount)}
                    </div>
                  </div>
                  <div>
                    <span className="text-sub">Thu ngân & Thời gian:</span>
                    <div className="font-semibold text-ink">
                      {receipt.collectedByStaffName ?? `#${receipt.collectedByStaffId}`} •{' '}
                      {formatPaidAt(receipt.paidAt)}
                    </div>
                  </div>
                </div>

                <ul className="space-y-1">
                  {receipt.allocations.map((a) => (
                    <li
                      key={a.visitChargeId}
                      className="flex items-center justify-between text-ink"
                    >
                      <span>{a.serviceName ?? `Khoản phí #${a.visitChargeId}`}</span>
                      <span className="font-mono font-semibold">
                        {formatVnd(a.allocatedAmount)}
                      </span>
                    </li>
                  ))}
                </ul>

                <div className="flex items-center justify-end gap-2 pt-2">
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    onClick={() => setReceipt(null)}
                  >
                    Thu tiếp dịch vụ khác
                  </Button>
                  <Button type="button" variant="primary" size="sm" onClick={onClose}>
                    Hoàn tất
                  </Button>
                </div>
              </div>
            ) : (
              <>
                <div className="divide-y divide-border rounded-xl border border-border bg-white max-h-72 overflow-y-auto">
                  {charges.charges.map((line, idx) => {
                    const status = line.lineSettlementStatus ?? 'UNPAID';
                    const sel = line.selectionStatus?.toUpperCase() ?? 'ACCEPTED';
                    const canCheck =
                      line.chargeId != null &&
                      status === 'UNPAID' &&
                      (line.unitPrice ?? 0) > 0 &&
                      sel !== 'DECLINED' &&
                      sel !== 'SKIPPED_AFTER_PAID' &&
                      sel !== 'REVIEW_REQUIRED';
                    const checked = line.chargeId != null && selectedIds.includes(line.chargeId);
                    const isOptional =
                      line.chargeType === 'DIAGNOSTIC' && line.requiredForConclusion === false;
                    const canSkipPaid =
                      isOptional && status === 'PAID' && line.diagnosticOrderId != null;
                    const canDeclineUnpaid =
                      isOptional && status === 'UNPAID' && line.diagnosticOrderId != null;

                    return (
                      <div key={line.chargeId ?? idx} className="p-3.5 space-y-2 text-xs">
                        <div className="flex items-center justify-between gap-3">
                          <label className="flex items-center gap-3 min-w-0 cursor-pointer">
                            {canCheck ? (
                              <input
                                type="checkbox"
                                aria-label={`Chọn thu tiền mặt ${line.serviceName ?? 'dịch vụ'}`}
                                checked={checked}
                                onChange={(e) =>
                                  setSelectedById((prev) => ({
                                    ...prev,
                                    [line.chargeId!]: e.target.checked,
                                  }))
                                }
                                className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
                              />
                            ) : null}
                            <div className="min-w-0">
                              <div className="flex flex-wrap items-center gap-1.5">
                                <span className="font-bold text-navy truncate">
                                  {line.serviceName ?? 'Khoản phí dịch vụ'}
                                </span>
                                {line.chargeType === 'DIAGNOSTIC' ? (
                                  <Badge tone={line.requiredForConclusion ? 'sky' : 'slate'}>
                                    {line.requiredForConclusion ? 'Bắt buộc' : 'Tùy chọn'}
                                  </Badge>
                                ) : null}
                                <Badge
                                  tone={
                                    status === 'PAID' || status === 'WAIVED'
                                      ? 'emerald'
                                      : status === 'UNPAID'
                                        ? 'amber'
                                        : 'slate'
                                  }
                                >
                                  {lineSettlementLabel(status)}
                                </Badge>
                              </div>
                            </div>
                          </label>

                          <div className="flex items-center gap-2 shrink-0">
                            <span className="font-mono font-bold text-ink">
                              {formatVnd(line.unitPrice)}
                            </span>
                            {(canSkipPaid || canDeclineUnpaid) && line.diagnosticOrderId != null ? (
                              <Button
                                type="button"
                                variant="secondary"
                                size="sm"
                                onClick={() => {
                                  setSkipConfirmOrderId(
                                    skipConfirmOrderId === line.diagnosticOrderId
                                      ? null
                                      : line.diagnosticOrderId,
                                  );
                                  setSkipReason('');
                                }}
                              >
                                <XCircle className="h-3.5 w-3.5" aria-hidden="true" />
                                <span>{canSkipPaid ? 'Bỏ qua sau TT' : 'Từ chối'}</span>
                              </Button>
                            ) : null}
                          </div>
                        </div>

                        {skipConfirmOrderId === line.diagnosticOrderId &&
                        line.diagnosticOrderId != null ? (
                          <div className="rounded-xl border border-amber-200 bg-amber-50/70 p-3 space-y-2">
                            <p className="font-semibold text-navy">
                              {canSkipPaid
                                ? 'Xác nhận bệnh nhân tự nguyện bỏ qua dịch vụ tùy chọn đã thanh toán (không hoàn tiền)?'
                                : 'Xác nhận bệnh nhân từ chối dịch vụ tùy chọn chưa thanh toán?'}
                            </p>
                            <input
                              type="text"
                              aria-label="Lý do xác nhận tại quầy"
                              value={skipReason}
                              onChange={(e) => setSkipReason(e.target.value)}
                              placeholder="Nhập lý do xác nhận..."
                              className="w-full rounded-lg border border-border bg-white px-3 py-1.5 text-xs text-ink focus:border-primary focus:outline-none"
                            />
                            <div className="flex justify-end gap-2">
                              <Button
                                type="button"
                                variant="secondary"
                                size="sm"
                                onClick={() => setSkipConfirmOrderId(null)}
                              >
                                Hủy
                              </Button>
                              <Button
                                type="button"
                                variant="danger"
                                size="sm"
                                loading={declineMutation.isPending}
                                onClick={() =>
                                  void declineMutation.mutateAsync({
                                    orderId: line.diagnosticOrderId!,
                                    confirmAbandonPaid: canSkipPaid,
                                    reason:
                                      skipReason.trim() ||
                                      (canSkipPaid
                                        ? 'Bệnh nhân tự nguyện bỏ qua dịch vụ tùy chọn đã thanh toán tại quầy.'
                                        : 'Bệnh nhân từ chối thực hiện dịch vụ tùy chọn tại quầy.'),
                                  })
                                }
                              >
                                Xác nhận
                              </Button>
                            </div>
                          </div>
                        ) : null}
                      </div>
                    );
                  })}
                </div>

                <div className="space-y-1">
                  <label
                    htmlFor="reception-cash-notes"
                    className="block text-[11px] font-bold text-navy"
                  >
                    Ghi chú phiếu thu (tùy chọn)
                  </label>
                  <input
                    id="reception-cash-notes"
                    type="text"
                    value={notes}
                    onChange={(e) => setNotes(e.target.value)}
                    placeholder="VD: Thu tiền mặt trực tiếp tại quầy tiếp nhận..."
                    className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none"
                  />
                </div>

                <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-3">
                  <div>
                    <span className="text-xs text-sub">
                      Tổng thu tiền mặt ({selectedIds.length} khoản):{' '}
                    </span>
                    <strong className="font-mono text-base text-navy">
                      {formatVnd(selectedTotal)}
                    </strong>
                  </div>
                  <div className="flex items-center gap-2">
                    <Button
                      type="button"
                      variant="secondary"
                      size="sm"
                      loading={chargesQuery.isFetching}
                      onClick={() => void chargesQuery.refetch()}
                    >
                      <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>Tải lại</span>
                    </Button>
                    <Button
                      type="button"
                      variant="primary"
                      size="sm"
                      disabled={selectedIds.length === 0}
                      loading={collectMutation.isPending}
                      onClick={() => void handleCollectCash()}
                    >
                      <Banknote className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>Xác nhận thu tiền mặt ({selectedIds.length})</span>
                    </Button>
                  </div>
                </div>
              </>
            )}
          </div>
        ) : null}
      </div>
    </Modal>
  );
}
