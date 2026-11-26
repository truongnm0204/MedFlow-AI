import { QrCode, RefreshCw } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { PaymentQrCard } from '@/features/payment/components/payment-qr-card';
import { useRemainingMs } from '@/features/payment/hooks/use-remaining-ms';
import type {
  GenerateQrPaymentResponse,
  VisitChargeLine,
  VisitChargesResponse,
} from '@/features/payment/types/payment.types';
import {
  PAYMENT_QR_TTL_MS,
  formatVnd,
  lineSettlementLabel,
} from '@/features/payment/utils/payment-format';
import { apiClient } from '@/shared/lib/axios';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';
import { Spinner } from '@/shared/ui/spinner';

export interface ConsultationVietQrModalProps {
  open: boolean;
  visitIdOrCode: string;
  patientName: string;
  onClose: () => void;
}

async function fetchDoctorVisitChargesApi(visitIdOrCode: string): Promise<VisitChargesResponse> {
  const { data } = await apiClient.get<VisitChargesResponse>(
    `/doctor/visits/${encodeURIComponent(visitIdOrCode.trim())}/charges`,
  );
  return data;
}

async function generateDoctorQrApi(
  visitIdOrCode: string,
  visitChargeIds: number[],
): Promise<GenerateQrPaymentResponse> {
  const { data } = await apiClient.post<GenerateQrPaymentResponse>(
    `/doctor/visits/${encodeURIComponent(visitIdOrCode.trim())}/payments/qr`,
    { visitChargeIds },
  );
  return data;
}

export function ConsultationVietQrModal({
  open,
  visitIdOrCode,
  patientName,
  onClose,
}: ConsultationVietQrModalProps) {
  const queryClient = useQueryClient();
  const [selectedById, setSelectedById] = useState<Record<number, boolean>>({});
  const [qr, setQr] = useState<GenerateQrPaymentResponse | null>(null);
  const [expiresAt, setExpiresAt] = useState<number | null>(null);
  const remainingMs = useRemainingMs(expiresAt);

  const chargesQuery = useQuery({
    queryKey: ['doctor-visit-charges', visitIdOrCode],
    queryFn: () => fetchDoctorVisitChargesApi(visitIdOrCode),
    enabled: open && Boolean(visitIdOrCode.trim()),
  });

  const qrMutation = useMutation({
    mutationFn: (chargeIds: number[]) => generateDoctorQrApi(visitIdOrCode, chargeIds),
    onSuccess: (created) => {
      setQr(created);
      setExpiresAt(Date.now() + PAYMENT_QR_TTL_MS);
      void queryClient.invalidateQueries({ queryKey: ['doctor-visit-charges', visitIdOrCode] });
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
      .map((l) => l.chargeId!);
  }, [charges, selectedById]);

  const selectedSum = useMemo(() => {
    if (!charges) return 0;
    const set = new Set(selectedIds);
    return charges.charges
      .filter((l) => l.chargeId != null && set.has(l.chargeId))
      .reduce((acc, l) => acc + (l.unitPrice ?? 0), 0);
  }, [charges, selectedIds]);

  const isExpired = expiresAt !== null && remainingMs === 0;

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-2xl">
      <div className="p-6 space-y-4">
        <div className="flex items-start justify-between gap-3 pr-8">
          <div className="flex items-center gap-2.5">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <QrCode className="h-5 w-5" aria-hidden="true" />
            </div>
            <div>
              <h2 className="text-base font-bold text-navy">
                Hiển thị VietQR thanh toán tại buồng khám
              </h2>
              <p className="text-xs text-sub">
                Bệnh nhân: <strong className="text-ink">{patientName}</strong> • Mã ca:{' '}
                <span className="font-mono font-semibold text-navy">{visitIdOrCode}</span>
              </p>
            </div>
          </div>
        </div>

        <p className="rounded-xl border border-border bg-canvas px-3.5 py-2 text-xs text-sub">
          Bác sĩ chỉ hiển thị mã VietQR cho các dịch vụ bệnh nhân đồng ý thực hiện. Trạng thái thanh
          toán được xác nhận tự động qua cổng ngân hàng (không xác nhận thủ công tại buồng khám).
        </p>

        {chargesQuery.isPending ? (
          <div className="flex items-center justify-center py-8">
            <Spinner />
          </div>
        ) : chargesQuery.isError ? (
          <Alert tone="error">{getErrorMessage(chargesQuery.error)}</Alert>
        ) : charges ? (
          <div className="space-y-4">
            {qrMutation.isError ? (
              <Alert tone="error">{getErrorMessage(qrMutation.error)}</Alert>
            ) : null}

            {qr ? (
              <div className="space-y-3">
                <PaymentQrCard qr={qr} remainingMs={remainingMs} isExpired={isExpired} />
                <div className="flex items-center justify-between gap-2">
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    onClick={() => {
                      setQr(null);
                      setExpiresAt(null);
                    }}
                  >
                    Chọn lại danh sách dịch vụ
                  </Button>
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    loading={chargesQuery.isFetching}
                    onClick={() => void chargesQuery.refetch()}
                  >
                    <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                    <span>Làm mới trạng thái</span>
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

                    return (
                      <label
                        key={line.chargeId ?? idx}
                        className="flex items-center justify-between gap-3 p-3.5 text-xs cursor-pointer hover:bg-canvas/60"
                      >
                        <div className="flex items-center gap-3 min-w-0">
                          {canCheck ? (
                            <input
                              type="checkbox"
                              aria-label={`Chọn tạo QR cho ${line.serviceName ?? 'dịch vụ'}`}
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
                                {line.serviceName ?? 'Dịch vụ khám'}
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
                        </div>
                        <span className="font-mono font-bold text-ink shrink-0">
                          {formatVnd(line.unitPrice)}
                        </span>
                      </label>
                    );
                  })}
                </div>

                <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-3">
                  <div className="text-xs text-sub">
                    Tổng tiền mã QR ({selectedIds.length} dịch vụ):{' '}
                    <strong className="font-mono text-sm text-navy">
                      {formatVnd(selectedSum)}
                    </strong>
                  </div>
                  <div className="flex items-center gap-2">
                    <Button type="button" variant="secondary" size="sm" onClick={onClose}>
                      Đóng
                    </Button>
                    <Button
                      type="button"
                      variant="primary"
                      size="sm"
                      disabled={selectedIds.length === 0}
                      loading={qrMutation.isPending}
                      onClick={() => void qrMutation.mutateAsync(selectedIds)}
                    >
                      <QrCode className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>Tạo mã VietQR ({selectedIds.length})</span>
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
