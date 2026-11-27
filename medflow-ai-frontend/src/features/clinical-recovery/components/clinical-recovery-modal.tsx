import { useEffect, useMemo, useState } from 'react';
import { AlertCircle, RotateCcw, ShieldCheck, Stethoscope } from 'lucide-react';
import { ZodError } from 'zod';

import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Modal } from '@/shared/ui/modal';
import { Spinner } from '@/shared/ui/spinner';

import {
  useClinicalRecoveryActions,
  useClinicalRecoveryContext,
  useClinicalRecoveryEligibleRecipients,
  useClinicalRecoveryList,
} from '../hooks/use-clinical-recovery';
import { getCoordinationErrorMessage } from '../lib/coordination-error-message';
import {
  confirmRecoverySchema,
  createRecoveryRequestSchema,
  grantRecoverySchema,
  recoveryDecisionSchema,
} from '../schemas/clinical-recovery.schema';
import type {
  ClinicalRecoveryDecisionPayload,
  ClinicalRecoveryReasonFields,
  ConfirmClinicalRecoveryRequestPayload,
  CreateClinicalRecoveryRequestPayload,
  GrantClinicalRecoveryRequestPayload,
} from '../types/clinical-recovery.types';
import { ClinicalRecoveryStatusBadge } from './clinical-recovery-status-badge';

export interface ClinicalRecoveryModalProps {
  open: boolean;
  initialVisitCode?: string | null;
  onClose: () => void;
  onApplied?: (message: string) => void;
}

type RecoveryAction = 'REQUEST' | 'GRANT' | 'REJECT' | 'REVOKE' | 'CANCEL' | 'CONFIRM';

const ACTION_TITLES: Record<RecoveryAction, string> = {
  REQUEST: 'Tạo yêu cầu phục hồi phiên khám',
  GRANT: 'Cấp uỷ quyền phục hồi cho bác sĩ khác',
  REJECT: 'Từ chối yêu cầu phục hồi',
  REVOKE: 'Thu hồi uỷ quyền phục hồi',
  CANCEL: 'Rút yêu cầu phục hồi',
  CONFIRM: 'Xác nhận phiên khám bị gián đoạn',
};

interface ActionTarget {
  action: RecoveryAction;
  requestId?: number;
  queueEntryId?: number;
  expectedStateVersion?: number;
}

const REASON_LABELS: Array<{ key: keyof ClinicalRecoveryReasonFields; label: string }> = [
  { key: 'requestReason', label: 'Lý do yêu cầu' },
  { key: 'grantReason', label: 'Lý do cấp uỷ quyền' },
  { key: 'recoveryReason', label: 'Lý do phục hồi' },
  { key: 'revocationReason', label: 'Lý do thu hồi' },
  { key: 'rejectionReason', label: 'Lý do từ chối' },
  { key: 'cancellationReason', label: 'Lý do rút yêu cầu' },
];

function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID().slice(0, 48);
  }
  return `clinical-recovery-${Date.now()}`;
}

function formatDateTime(value: string | null): string {
  if (!value) return '—';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString('vi-VN');
}

/** PHI warning repeated verbatim next to every free-text reason field. */
const PHI_WARNING =
  'Không nhập thông tin nhận diện bệnh nhân (họ tên, mã bệnh nhân, SĐT, địa chỉ). Lý do được người khác đọc để đánh giá độc lập và lưu trong nhật ký kiểm toán.';

function ReasonList({ reasons }: { reasons: ClinicalRecoveryReasonFields }) {
  const entries = REASON_LABELS.filter(({ key }) => Boolean(reasons[key]?.trim()));
  if (entries.length === 0) return null;
  return (
    <dl className="mt-2 space-y-1 border-t border-border pt-2">
      {entries.map(({ key, label }) => (
        <div key={key} className="grid grid-cols-[9rem_1fr] gap-2">
          <dt className="text-[11px] font-semibold uppercase tracking-wide text-sub">{label}</dt>
          <dd className="text-xs text-ink">{reasons[key]}</dd>
        </div>
      ))}
    </dl>
  );
}

export function ClinicalRecoveryModal({
  open,
  initialVisitCode,
  onClose,
  onApplied,
}: ClinicalRecoveryModalProps) {
  const [visitCodeInput, setVisitCodeInput] = useState(initialVisitCode?.trim() ?? '');
  const [activeVisitCode, setActiveVisitCode] = useState('');
  const [target, setTarget] = useState<ActionTarget | null>(null);
  const [reason, setReason] = useState('');
  const [confirmInterrupted, setConfirmInterrupted] = useState(false);
  const [recipientDoctorId, setRecipientDoctorId] = useState('');
  const [idempotencyKey, setIdempotencyKey] = useState('');
  const [formError, setFormError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const contextQuery = useClinicalRecoveryContext(activeVisitCode);
  const listQuery = useClinicalRecoveryList(open && !activeVisitCode);
  const actions = useClinicalRecoveryActions(activeVisitCode);
  const context = contextQuery.data;

  const recipientsQuery = useClinicalRecoveryEligibleRecipients(
    target?.action === 'GRANT' ? (target.requestId ?? null) : null,
  );

  const pending =
    actions.request.isPending ||
    actions.grant.isPending ||
    actions.reject.isPending ||
    actions.revoke.isPending ||
    actions.cancel.isPending ||
    actions.confirm.isPending;

  const clinicalQueues = useMemo(
    () =>
      (context?.queues ?? []).filter(
        (queue) => queue.queueType === 'EXAMINATION' || queue.queueType === 'RETURN_CONSULTATION',
      ),
    [context],
  );

  const unresolvedRequests = useMemo(
    () =>
      (context?.requests ?? []).filter((r) => r.status === 'REQUESTED' || r.status === 'GRANTED'),
    [context],
  );

  useEffect(() => {
    if (!open) {
      setTarget(null);
      setReason('');
      setConfirmInterrupted(false);
      setRecipientDoctorId('');
      setFormError(null);
      setNotice(null);
    }
  }, [open]);

  useEffect(() => {
    const seed = initialVisitCode?.trim();
    if (open && seed && seed !== visitCodeInput) {
      setVisitCodeInput(seed);
      setActiveVisitCode(seed);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, initialVisitCode]);

  const loadVisitContext = (visitCode: string) => {
    const trimmed = visitCode.trim();
    if (!trimmed) return;
    setVisitCodeInput(trimmed);
    setActiveVisitCode(trimmed);
    setTarget(null);
    setNotice(null);
  };

  const startAction = (next: ActionTarget) => {
    setTarget(next);
    setReason('');
    setFormError(null);
    setNotice(null);
    setConfirmInterrupted(false);
    setRecipientDoctorId('');
    setIdempotencyKey(next.action === 'CONFIRM' ? newIdempotencyKey() : '');
  };

  const finish = (message: string) => {
    setTarget(null);
    setReason('');
    setNotice(message);
    onApplied?.(message);
  };

  const handleSubmit = async () => {
    if (!target) return;
    setFormError(null);

    try {
      if (target.action === 'REQUEST') {
        const payload: CreateClinicalRecoveryRequestPayload = {
          queueEntryId: target.queueEntryId ?? 0,
          expectedStateVersion: target.expectedStateVersion ?? 0,
          reason,
        };
        createRecoveryRequestSchema.parse(payload);
        await actions.request.mutateAsync(payload);
        finish('Đã tạo yêu cầu phục hồi. Phiên khám và hàng đợi chưa bị thay đổi.');
        return;
      }

      if (target.action === 'GRANT') {
        const payload: GrantClinicalRecoveryRequestPayload = {
          authorizedDoctorId: Number(recipientDoctorId),
          expectedStateVersion: target.expectedStateVersion ?? 0,
          reason,
        };
        grantRecoverySchema.parse(payload);
        await actions.grant.mutateAsync({ requestId: target.requestId ?? 0, payload });
        finish('Đã cấp uỷ quyền phục hồi (hiệu lực 2 giờ, dùng một lần).');
        return;
      }

      if (target.action === 'CONFIRM') {
        const payload: ConfirmClinicalRecoveryRequestPayload = {
          expectedStateVersion: target.expectedStateVersion ?? 0,
          confirmInterrupted,
          reason,
          idempotencyKey: idempotencyKey || undefined,
        };
        confirmRecoverySchema.parse(payload);
        const result = await actions.confirm.mutateAsync({
          requestId: target.requestId ?? 0,
          payload,
        });
        finish(
          `Đã phục hồi phiên khám${result.isIdempotentReplay ? ' (lần gửi trước đã thành công)' : ''}. Hàng đợi đã được giải phóng.`,
        );
        return;
      }

      const payload: ClinicalRecoveryDecisionPayload = {
        reason,
        expectedStateVersion: target.expectedStateVersion,
      };
      recoveryDecisionSchema.parse(payload);
      const requestId = target.requestId ?? 0;
      if (target.action === 'REJECT') {
        await actions.reject.mutateAsync({ requestId, payload });
        finish('Đã từ chối yêu cầu phục hồi.');
      } else if (target.action === 'REVOKE') {
        await actions.revoke.mutateAsync({ requestId, payload });
        finish('Đã thu hồi uỷ quyền phục hồi.');
      } else {
        await actions.cancel.mutateAsync({ requestId, payload });
        finish('Đã rút yêu cầu phục hồi.');
      }
    } catch (error) {
      setFormError(
        error instanceof ZodError
          ? (error.issues[0]?.message ?? 'Dữ liệu chưa hợp lệ.')
          : getCoordinationErrorMessage(error, 'Không thực hiện được thao tác phục hồi.'),
      );
    }
  };

  const contextError = contextQuery.isError
    ? getCoordinationErrorMessage(contextQuery.error, 'Không tải được ngữ cảnh phục hồi.')
    : null;

  const listError = listQuery.isError
    ? getCoordinationErrorMessage(listQuery.error, 'Không tải được danh sách yêu cầu phục hồi.')
    : null;

  const requestsInScope = listQuery.data ?? [];

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-4xl">
      <div className="flex items-start justify-between gap-4 border-b border-border pb-4">
        <div>
          <h2 className="flex items-center gap-2 text-base font-bold text-navy">
            <RotateCcw className="h-4 w-4" aria-hidden="true" />
            Phục hồi phiên khám lâm sàng
          </h2>
          <p className="mt-1 text-xs text-sub">
            Chỉ giải phóng vé EXAMINATION / RETURN_CONSULTATION bị gián đoạn. Không hoàn tất lượt
            khám, không sửa hồ sơ, không cấp vé mới.
          </p>
        </div>
      </div>

      {/* Scoped list as the entry point — picking a row loads the visit context. */}
      {!activeVisitCode && (
        <section className="mt-4">
          <h3 className="mb-2 text-sm font-semibold text-ink">
            Yêu cầu phục hồi trong phạm vi của bạn
          </h3>
          {listQuery.isFetching && (
            <div className="flex items-center gap-2 text-xs text-sub">
              <Spinner /> Đang tải danh sách yêu cầu…
            </div>
          )}
          {listError && <Alert tone="error">{listError}</Alert>}
          {!listQuery.isFetching && !listError && requestsInScope.length === 0 && (
            <p className="text-xs text-sub">
              Không có yêu cầu phục hồi nào trong phạm vi của bạn. Nhập mã lượt khám bên dưới để tạo
              yêu cầu mới cho một vé bị gián đoạn.
            </p>
          )}
          {requestsInScope.length > 0 && (
            <ul className="max-h-56 space-y-1 overflow-y-auto rounded-lg border border-border">
              {requestsInScope.map((item) => (
                <li key={item.requestId}>
                  <button
                    type="button"
                    onClick={() => loadVisitContext(item.visitCode)}
                    className="flex w-full flex-wrap items-center justify-between gap-3 px-3 py-2 text-left text-xs transition-colors hover:bg-canvas"
                  >
                    <span className="flex flex-wrap items-center gap-3">
                      <span className="font-semibold text-ink">#{item.requestId}</span>
                      <ClinicalRecoveryStatusBadge status={item.status} />
                      <span className="text-sub">{item.visitCode}</span>
                      <span className="text-sub">Tạo lúc {formatDateTime(item.requestedAt)}</span>
                      {item.expiresAt && (
                        <span className="text-sub">Hết hạn {formatDateTime(item.expiresAt)}</span>
                      )}
                    </span>
                    <span className="text-[11px] font-semibold text-primary">Mở lượt khám</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}

      <div className="mt-4 flex items-end gap-3">
        <Input
          label="Mã lượt khám"
          placeholder="VD: VISIT-2026-000123"
          value={visitCodeInput}
          onChange={(event) => setVisitCodeInput(event.target.value)}
        />
        <Button
          variant="secondary"
          size="sm"
          onClick={() => loadVisitContext(visitCodeInput)}
          disabled={visitCodeInput.trim().length === 0}
        >
          Tải ngữ cảnh
        </Button>
        {activeVisitCode && (
          <Button
            variant="ghost"
            size="sm"
            onClick={() => {
              setActiveVisitCode('');
              setTarget(null);
              setNotice(null);
            }}
          >
            Quay lại danh sách
          </Button>
        )}
      </div>

      {contextQuery.isFetching && (
        <div className="mt-4 flex items-center gap-2 text-xs text-sub">
          <Spinner /> Đang tải ngữ cảnh phục hồi…
        </div>
      )}

      {contextError && (
        <div className="mt-4">
          <Alert tone="error">{contextError}</Alert>
        </div>
      )}

      {notice && (
        <div className="mt-4">
          <Alert tone="success">{notice}</Alert>
        </div>
      )}

      {context && (
        <div className="mt-4 space-y-4">
          <div className="rounded-lg border border-border bg-canvas px-4 py-3 text-xs text-ink">
            <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
              <span>
                <span className="text-sub">Lượt khám: </span>
                <span className="font-semibold">{context.visitCode}</span>
              </span>
              <span>
                <span className="text-sub">Trạng thái: </span>
                <span className="font-semibold">{context.visitStatus}</span>
              </span>
              <span>
                <span className="text-sub">Bác sĩ phụ trách: </span>
                <span className="font-semibold">
                  {context.assignedDoctorName ?? context.assignedDoctorCode ?? 'Chưa phân công'}
                </span>
              </span>
            </div>
          </div>

          <section>
            <h3 className="mb-2 flex items-center gap-2 text-sm font-semibold text-ink">
              <Stethoscope className="h-4 w-4" aria-hidden="true" />
              Vé khám lâm sàng
            </h3>
            {clinicalQueues.length === 0 ? (
              <p className="text-xs text-sub">Không có vé khám lâm sàng nào trong lượt khám này.</p>
            ) : (
              <ul className="space-y-2">
                {clinicalQueues.map((queue) => (
                  <li
                    key={queue.queueEntryId}
                    className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-border bg-white px-3 py-2 text-xs"
                  >
                    <div className="flex flex-wrap items-center gap-3">
                      <span className="font-semibold text-ink">{queue.queueType}</span>
                      <Badge tone="slate">{queue.status}</Badge>
                      <span className="text-sub">Phiên bản {queue.stateVersion}</span>
                      {queue.completedAt && (
                        <span className="text-sub">
                          Đóng lúc {formatDateTime(queue.completedAt)}
                        </span>
                      )}
                    </div>
                    {context.capabilities.canRequest && (
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() =>
                          startAction({
                            action: 'REQUEST',
                            queueEntryId: queue.queueEntryId,
                            expectedStateVersion: queue.stateVersion,
                          })
                        }
                      >
                        Tạo yêu cầu phục hồi
                      </Button>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section>
            <h3 className="mb-2 flex items-center gap-2 text-sm font-semibold text-ink">
              <ShieldCheck className="h-4 w-4" aria-hidden="true" />
              Yêu cầu phục hồi
            </h3>
            {context.requests.length === 0 ? (
              <p className="text-xs text-sub">Chưa có yêu cầu phục hồi nào cho lượt khám này.</p>
            ) : (
              <ul className="space-y-2">
                {context.requests.map((request) => {
                  const isUnresolved =
                    request.status === 'REQUESTED' || request.status === 'GRANTED';
                  return (
                    <li
                      key={request.requestId}
                      className="rounded-lg border border-border bg-white px-3 py-2 text-xs"
                    >
                      <div className="flex flex-wrap items-center justify-between gap-3">
                        <div className="flex flex-wrap items-center gap-3">
                          <span className="font-semibold text-ink">#{request.requestId}</span>
                          <ClinicalRecoveryStatusBadge status={request.status} />
                          {request.ownSession && <Badge tone="sky">Phiên của chính bác sĩ</Badge>}
                          <span className="text-sub">
                            Tạo lúc {formatDateTime(request.requestedAt)}
                          </span>
                          {request.expiresAt && (
                            <span className="text-sub">
                              Hết hạn {formatDateTime(request.expiresAt)}
                            </span>
                          )}
                        </div>
                        {isUnresolved && (
                          <div className="flex flex-wrap items-center gap-2">
                            {context.capabilities.canGrant && request.status === 'REQUESTED' && (
                              <Button
                                variant="secondary"
                                size="sm"
                                onClick={() =>
                                  startAction({
                                    action: 'GRANT',
                                    requestId: request.requestId,
                                    expectedStateVersion: request.expectedQueueStateVersion,
                                  })
                                }
                              >
                                Cấp uỷ quyền
                              </Button>
                            )}
                            {(context.capabilities.canConfirmOwnSession ||
                              context.capabilities.canConfirmDelegated) && (
                              <Button
                                variant="primary"
                                size="sm"
                                onClick={() =>
                                  startAction({
                                    action: 'CONFIRM',
                                    requestId: request.requestId,
                                    expectedStateVersion: request.expectedQueueStateVersion,
                                  })
                                }
                              >
                                Xác nhận gián đoạn
                              </Button>
                            )}
                            {context.capabilities.canReject && (
                              <Button
                                variant="secondary"
                                size="sm"
                                onClick={() =>
                                  startAction({
                                    action: 'REJECT',
                                    requestId: request.requestId,
                                    expectedStateVersion: request.expectedQueueStateVersion,
                                  })
                                }
                              >
                                Từ chối
                              </Button>
                            )}
                            {context.capabilities.canRevoke && request.status === 'GRANTED' && (
                              <Button
                                variant="secondary"
                                size="sm"
                                onClick={() =>
                                  startAction({
                                    action: 'REVOKE',
                                    requestId: request.requestId,
                                    expectedStateVersion: request.expectedQueueStateVersion,
                                  })
                                }
                              >
                                Thu hồi
                              </Button>
                            )}
                            {context.capabilities.canCancel && request.status === 'REQUESTED' && (
                              <Button
                                variant="secondary"
                                size="sm"
                                onClick={() =>
                                  startAction({
                                    action: 'CANCEL',
                                    requestId: request.requestId,
                                    expectedStateVersion: request.expectedQueueStateVersion,
                                  })
                                }
                              >
                                Rút yêu cầu
                              </Button>
                            )}
                          </div>
                        )}
                      </div>
                      <ReasonList reasons={request} />
                    </li>
                  );
                })}
              </ul>
            )}
            {unresolvedRequests.length > 0 && (
              <p className="mt-2 text-[11px] text-sub">
                Chỉ một yêu cầu chưa kết thúc trên mỗi vé; yêu cầu mới bị chặn cho tới khi yêu cầu
                hiện tại được xử lý.
              </p>
            )}
          </section>

          {target && (
            <section className="rounded-lg border border-border bg-canvas px-4 py-3">
              <h3 className="mb-2 text-sm font-semibold text-ink">
                {ACTION_TITLES[target.action]}
              </h3>

              {target.action === 'GRANT' && (
                <div>
                  <label
                    htmlFor="recovery-recipient"
                    className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-700"
                  >
                    Bác sĩ nhận uỷ quyền
                  </label>
                  {recipientsQuery.isFetching ? (
                    <div className="flex items-center gap-2 text-xs text-sub">
                      <Spinner /> Đang tải danh sách bác sĩ đủ điều kiện…
                    </div>
                  ) : recipientsQuery.isError ? (
                    <Alert tone="error">
                      {getCoordinationErrorMessage(
                        recipientsQuery.error,
                        'Không tải được danh sách bác sĩ đủ điều kiện.',
                      )}
                    </Alert>
                  ) : (recipientsQuery.data ?? []).length === 0 ? (
                    <Alert tone="warning">
                      Không có bác sĩ nào đủ điều kiện (cùng bệnh viện, cùng chuyên khoa, có quyền
                      phục hồi). Không thể cấp uỷ quyền cho yêu cầu này.
                    </Alert>
                  ) : (
                    <select
                      id="recovery-recipient"
                      className="w-full rounded-lg border border-border bg-white px-3 py-2 text-sm text-ink focus:border-primary focus:outline-none"
                      value={recipientDoctorId}
                      onChange={(event) => setRecipientDoctorId(event.target.value)}
                    >
                      <option value="">— Chọn bác sĩ —</option>
                      {(recipientsQuery.data ?? []).map((recipient) => (
                        <option key={recipient.doctorId} value={recipient.doctorId}>
                          {recipient.doctorName} ({recipient.doctorCode})
                          {recipient.departmentName ? ` — ${recipient.departmentName}` : ''}
                          {recipient.specialtyName ? ` · ${recipient.specialtyName}` : ''}
                        </option>
                      ))}
                    </select>
                  )}
                </div>
              )}

              {target.action === 'CONFIRM' && (
                <label className="mt-2 flex items-start gap-2 text-xs text-ink">
                  <input
                    type="checkbox"
                    className="mt-0.5"
                    checked={confirmInterrupted}
                    onChange={(event) => setConfirmInterrupted(event.target.checked)}
                  />
                  <span>
                    Tôi xác nhận phiên khám trước đã thực sự bị gián đoạn và không còn được thực
                    hiện trực tiếp.
                  </span>
                </label>
              )}

              <div className="mt-3">
                <Alert tone="warning">{PHI_WARNING}</Alert>
                <label className="mt-2 block text-xs font-semibold uppercase tracking-wide text-slate-700">
                  Lý do
                  <textarea
                    className="mt-2 w-full rounded-lg border border-border bg-white px-3 py-2 text-sm text-ink focus:border-primary focus:outline-none"
                    rows={3}
                    value={reason}
                    onChange={(event) => setReason(event.target.value)}
                    placeholder="Nêu căn cứ nghiệp vụ, 3–500 ký tự, không chứa ký tự < hoặc >."
                  />
                </label>
              </div>

              {formError && (
                <div className="mt-3">
                  <Alert tone="error">{formError}</Alert>
                </div>
              )}

              <div className="mt-3 flex items-center gap-2">
                <Button
                  variant="primary"
                  size="sm"
                  onClick={() => void handleSubmit()}
                  disabled={
                    pending ||
                    (target.action === 'GRANT' &&
                      ((recipientsQuery.data ?? []).length === 0 || !recipientDoctorId))
                  }
                >
                  {pending ? (
                    <Spinner />
                  ) : (
                    <AlertCircle className="h-3.5 w-3.5" aria-hidden="true" />
                  )}
                  <span>Xác nhận thao tác</span>
                </Button>
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => setTarget(null)}
                  disabled={pending}
                >
                  Huỷ
                </Button>
              </div>
            </section>
          )}
        </div>
      )}

      <div className="mt-4 flex justify-end">
        <Button variant="secondary" size="sm" onClick={onClose}>
          Đóng
        </Button>
      </div>
    </Modal>
  );
}
