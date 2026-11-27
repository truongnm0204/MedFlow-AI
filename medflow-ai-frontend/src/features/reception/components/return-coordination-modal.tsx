import { AlertTriangle, CheckCircle2, Search, Star, Stethoscope, UserCheck } from 'lucide-react';
import { type FormEvent, useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  extractProblemCode,
  getCoordinationErrorMessage,
} from '@/features/clinical-recovery/lib/coordination-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';
import { Spinner } from '@/shared/ui/spinner';

import {
  enrollReturnConsultationApi,
  fetchReturnCoordinationContextApi,
} from '../api/return-coordination.api';
import type {
  ReturnCoordinationPurpose,
  ReturnEnrollmentResponseDto,
} from '../types/return-coordination.types';

export interface ReturnCoordinationModalProps {
  open: boolean;
  initialVisitCode?: string | null;
  onClose: () => void;
  onEnrolled?: (result: ReturnEnrollmentResponseDto) => void;
  /**
   * Fired when enrollment is blocked by 409 `CLINICAL_HANDOFF_REQUIRED`: the prior clinical
   * session was never released, so the actor must resolve a clinical-session-recovery request
   * before coordination. The parent hands off to the recovery modal for the same visit.
   */
  onRecoveryRequired?: (visitCode: string) => void;
}

const PRESENCE_BADGE: Record<string, { label: string; tone: 'emerald' | 'amber' | 'slate' }> = {
  PRESENT: { label: 'Có mặt hôm nay', tone: 'emerald' },
  LEFT: { label: 'Đã rời viện', tone: 'slate' },
  DEFERRED: { label: 'Đang tạm hoãn', tone: 'amber' },
};

const PURPOSE_LABELS: Record<ReturnCoordinationPurpose, string> = {
  RESULT_CONCLUSION: 'Khám kết luận (đọc kết quả)',
  CLINICAL_REVIEW: 'Đánh giá lại (chỉ định hết hạn / cần xem xét)',
};

function generateIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID().slice(0, 48);
  }
  return `return-enroll-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

/**
 * Centralized Reception return-coordination modal (Stage E2 / RC-01..22).
 * Reception is the ONLY actor allowed to coordinate the return-consult doctor and issue the
 * RETURN_CONSULTATION ticket — doctor and patient UI never mint this ticket themselves.
 */
export function ReturnCoordinationModal({
  open,
  initialVisitCode,
  onClose,
  onEnrolled,
  onRecoveryRequired,
}: ReturnCoordinationModalProps) {
  const queryClient = useQueryClient();
  const [searchInput, setSearchInput] = useState(initialVisitCode?.trim() ?? '');
  const [activeVisitKey, setActiveVisitKey] = useState(initialVisitCode?.trim() ?? '');
  const [purpose, setPurpose] = useState<ReturnCoordinationPurpose>('RESULT_CONCLUSION');
  const [selectedDoctorId, setSelectedDoctorId] = useState<string>('');
  const [reassignmentReason, setReassignmentReason] = useState('');
  const [notes, setNotes] = useState('');
  const [confirmPresent, setConfirmPresent] = useState(false);
  const [localValidationError, setLocalValidationError] = useState<string | null>(null);

  const idempotencyKeyRef = useRef<string | null>(null);

  const contextQuery = useQuery({
    queryKey: ['return-coordination-context', activeVisitKey],
    queryFn: () => fetchReturnCoordinationContextApi(activeVisitKey),
    enabled: open && Boolean(activeVisitKey),
    retry: false,
  });

  const ctx = contextQuery.data ?? null;

  // Rank eligible candidates by projected load (ASC), original assigned doctor first.
  const rankedDoctors = useMemo(() => {
    if (!ctx) return [];
    return [...ctx.eligibleDoctors].sort((a, b) => {
      if (a.isEligible !== b.isEligible) return a.isEligible ? -1 : 1;
      if (a.isOriginalAssignedDoctor !== b.isOriginalAssignedDoctor)
        return a.isOriginalAssignedDoctor ? -1 : 1;
      return a.projectedLoadCount - b.projectedLoadCount;
    });
  }, [ctx]);

  const selectedDoctor = useMemo(
    () => rankedDoctors.find((d) => String(d.doctorId) === selectedDoctorId) ?? null,
    [rankedDoctors, selectedDoctorId],
  );

  const isReassigning =
    selectedDoctor !== null &&
    ctx?.currentAssignedDoctorId != null &&
    selectedDoctor.doctorId !== ctx.currentAssignedDoctorId;

  const needsReassignmentReason = isReassigning && ctx?.allowDoctorReassignment === true;

  const enrollMutation = useMutation({
    mutationFn: () =>
      enrollReturnConsultationApi(activeVisitKey, {
        doctorId: selectedDoctor ? selectedDoctor.doctorId : null,
        purpose,
        reassignmentReason: needsReassignmentReason ? reassignmentReason.trim() || null : null,
        notes: notes.trim() || null,
        idempotencyKey: idempotencyKeyRef.current,
        confirmPresent,
      }),
    onSuccess: (result) => {
      void queryClient.invalidateQueries({ queryKey: ['return-coordination-context'] });
      void queryClient.invalidateQueries({ queryKey: ['reception-examination-queue'] });
      onEnrolled?.(result);
    },
  });

  function handleSearch(e: FormEvent) {
    e.preventDefault();
    const key = searchInput.trim();
    if (!key) return;
    setActiveVisitKey(key);
    setLocalValidationError(null);
    setSelectedDoctorId('');
    idempotencyKeyRef.current = generateIdempotencyKey();
  }

  function handleEnroll(e: FormEvent) {
    e.preventDefault();
    setLocalValidationError(null);
    if (!ctx) return;
    if (needsReassignmentReason && !reassignmentReason.trim()) {
      setLocalValidationError(
        'Bạn đang đổi bác sĩ phụ trách — vui lòng nhập Lý do chuyển bác sĩ (ReassignmentReason).',
      );
      return;
    }
    enrollMutation.mutate();
  }

  const enrollErrorCode = enrollMutation.isError ? extractProblemCode(enrollMutation.error) : null;
  const isHandoffBlocked = enrollErrorCode === 'CLINICAL_HANDOFF_REQUIRED';
  const isPatientNotPresent = enrollErrorCode === 'PATIENT_NOT_PRESENT';

  const apiError = enrollMutation.isError
    ? getCoordinationErrorMessage(enrollMutation.error)
    : contextQuery.isError
      ? getCoordinationErrorMessage(contextQuery.error)
      : null;

  const presence = ctx ? (PRESENCE_BADGE[ctx.presenceState] ?? PRESENCE_BADGE.LEFT) : null;

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-3xl">
      <div className="space-y-4 p-6">
        <header className="flex items-start justify-between gap-4">
          <div>
            <h2 className="text-lg font-semibold text-ink">Điều phối khám kết luận</h2>
            <p className="text-sm text-sub">
              Cấp số RETURN_CONSULTATION và điều phối bác sĩ — chỉ Quầy Tiếp đón thực hiện.
            </p>
          </div>
        </header>

        {/* Visit lookup */}
        <form onSubmit={handleSearch} className="flex gap-2">
          <input
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            placeholder="Nhập mã lượt khám (vd: VIS-202611-0001)"
            className="flex-1 rounded-lg border border-border bg-canvas px-3 py-2 text-sm text-ink"
            aria-label="Mã lượt khám"
          />
          <Button type="submit" disabled={contextQuery.isFetching}>
            <Search className="size-4" aria-hidden="true" />
            Tra cứu
          </Button>
        </form>

        {contextQuery.isFetching && (
          <div className="flex items-center gap-2 text-sm text-sub">
            <Spinner /> Đang tải ngữ cảnh điều phối…
          </div>
        )}

        {apiError && (
          <Alert tone="error">
            <p>{apiError}</p>
            {isPatientNotPresent && (
              <p className="mt-1">
                Bệnh nhân chưa được xác nhận có mặt. Đánh dấu “Xác nhận bệnh nhân có mặt tại quầy”
                bên dưới rồi thao tác lại — không bỏ qua bước này khi bệnh nhân chưa đến.
              </p>
            )}
            {isHandoffBlocked && onRecoveryRequired && (
              <div className="mt-2">
                <Button
                  type="button"
                  variant="secondary"
                  size="sm"
                  onClick={() => onRecoveryRequired(ctx?.visitCode ?? activeVisitKey)}
                >
                  Mở phục hồi phiên khám
                </Button>
              </div>
            )}
          </Alert>
        )}
        {localValidationError && <Alert tone="error">{localValidationError}</Alert>}

        {ctx && (
          <div className="space-y-4">
            {/* Visit summary */}
            <div className="rounded-lg border border-border bg-surface p-4">
              <div className="flex flex-wrap items-center gap-3">
                <span className="font-semibold text-ink">{ctx.visitCode}</span>
                {presence && <Badge tone={presence.tone}>{presence.label}</Badge>}
                <Badge tone={ctx.isReadyForReturnConclusion ? 'emerald' : 'amber'}>
                  {ctx.isReadyForReturnConclusion ? 'Sẵn sàng khám kết luận' : 'Chưa đủ kết quả'}
                </Badge>
                {ctx.isReturnOverdue && (
                  <Badge tone="amber">
                    <AlertTriangle className="mr-1 inline size-3" aria-hidden="true" />
                    Quá hạn quay lại
                  </Badge>
                )}
              </div>
              {ctx.currentAssignedDoctorName && (
                <p className="mt-2 text-sm text-sub">
                  Bác sĩ phụ trách hiện tại:{' '}
                  <span className="font-medium text-ink">
                    {ctx.currentAssignedDoctorName} ({ctx.currentAssignedDoctorCode})
                  </span>
                  {ctx.currentExaminationRoomCode && ` — Phòng ${ctx.currentExaminationRoomCode}`}
                </p>
              )}
              {ctx.activeQueueEntry && (
                <p className="mt-1 text-sm text-sub">
                  Đã có số #{ctx.activeQueueEntry.queueNumber} ({ctx.activeQueueEntry.status})
                </p>
              )}
            </div>

            <form onSubmit={handleEnroll} className="space-y-4">
              {/* Purpose */}
              <div>
                <label className="mb-1 block text-sm font-medium text-ink">
                  Mục đích điều phối
                </label>
                <div className="grid grid-cols-2 gap-2">
                  {(Object.keys(PURPOSE_LABELS) as ReturnCoordinationPurpose[]).map((p) => (
                    <button
                      key={p}
                      type="button"
                      onClick={() => setPurpose(p)}
                      className={`rounded-lg border px-3 py-2 text-left text-sm ${
                        purpose === p
                          ? 'border-primary bg-primary/5 font-medium text-primary'
                          : 'border-border bg-canvas text-sub'
                      }`}
                    >
                      {PURPOSE_LABELS[p]}
                      {p === 'CLINICAL_REVIEW' && ctx.hasClinicalReviewItems && (
                        <Badge tone="amber" className="ml-2">
                          Có chỉ định cần xem xét
                        </Badge>
                      )}
                    </button>
                  ))}
                </div>
              </div>

              {/* Doctor candidates */}
              <div>
                <label className="mb-1 block text-sm font-medium text-ink">
                  Chọn bác sĩ <span className="font-normal text-sub">(xếp theo tải dự kiến)</span>
                </label>
                <div className="max-h-64 space-y-1 overflow-y-auto rounded-lg border border-border">
                  {rankedDoctors.length === 0 && (
                    <p className="p-3 text-sm text-sub">Không có bác sĩ khả dụng.</p>
                  )}
                  {rankedDoctors.map((d) => {
                    const disabled = !d.isEligible;
                    const selected = selectedDoctorId === String(d.doctorId);
                    return (
                      <label
                        key={d.doctorId}
                        className={`flex cursor-pointer items-center gap-3 px-3 py-2 text-sm ${
                          disabled ? 'cursor-not-allowed opacity-50' : 'hover:bg-surface'
                        } ${selected ? 'bg-primary/5' : ''}`}
                      >
                        <input
                          type="radio"
                          name="return-doctor"
                          value={d.doctorId}
                          disabled={disabled}
                          checked={selected}
                          onChange={(e) => setSelectedDoctorId(e.target.value)}
                          className="size-4"
                        />
                        <div className="min-w-0 flex-1">
                          <div className="flex items-center gap-2">
                            <span className="font-medium text-ink">{d.doctorName}</span>
                            {d.isRecommended && (
                              <Badge tone="emerald">
                                <Star className="mr-1 inline size-3" aria-hidden="true" />
                                Đề xuất
                              </Badge>
                            )}
                            {d.isOriginalAssignedDoctor && (
                              <Badge tone="slate">
                                <UserCheck className="mr-1 inline size-3" aria-hidden="true" />
                                Bác sĩ gốc
                              </Badge>
                            )}
                            {d.isAtCapacity && <Badge tone="amber">Đã đầy</Badge>}
                          </div>
                          <div className="text-xs text-sub">
                            {[d.departmentName, d.roomCode && `Phòng ${d.roomCode}`]
                              .filter(Boolean)
                              .join(' · ') || '—'}
                            {` · Tải dự kiến: ${d.projectedLoadCount}`}
                            {d.remainingShiftMinutes != null &&
                              ` · Còn ${d.remainingShiftMinutes}p`}
                          </div>
                          {disabled && d.ineligibleReasons.length > 0 && (
                            <div className="mt-0.5 text-xs text-warning">
                              {d.ineligibleReasons.join('; ')}
                            </div>
                          )}
                        </div>
                        <Stethoscope className="size-4 text-sub" aria-hidden="true" />
                      </label>
                    );
                  })}
                </div>
              </div>

              {/* Reassignment reason (required when switching doctor) */}
              {needsReassignmentReason && (
                <div>
                  <label className="mb-1 block text-sm font-medium text-ink">
                    Lý do chuyển bác sĩ <span className="text-error">*</span>
                  </label>
                  <input
                    value={reassignmentReason}
                    onChange={(e) => setReassignmentReason(e.target.value)}
                    placeholder="vd: Bác sĩ gốc hết ca / chuyển khoa"
                    className="w-full rounded-lg border border-border bg-canvas px-3 py-2 text-sm text-ink"
                    required={needsReassignmentReason}
                  />
                </div>
              )}

              {/* Notes */}
              <div>
                <label className="mb-1 block text-sm font-medium text-ink">Ghi chú</label>
                <textarea
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  rows={2}
                  className="w-full rounded-lg border border-border bg-canvas px-3 py-2 text-sm text-ink"
                  placeholder="Ghi chú cho lễ tân/bác sĩ (tùy chọn)"
                />
              </div>

              {/* §8.2: presence is explicit — never default-confirmed, no legacy NOT_ARRIVED fallback. */}
              <div className="rounded-lg border border-border bg-canvas px-3 py-2">
                <label className="flex items-start gap-2 text-sm text-ink">
                  <input
                    type="checkbox"
                    className="mt-0.5"
                    checked={confirmPresent}
                    onChange={(e) => setConfirmPresent(e.target.checked)}
                  />
                  <span>
                    <span className="font-medium">Xác nhận bệnh nhân có mặt tại quầy.</span>{' '}
                    <span className="text-sub">
                      Chỉ đánh dấu khi bệnh nhân thực sự đang ở quầy. Nếu chính sách bệnh viện yêu
                      cầu xác nhận có mặt và bệnh nhân chưa đến, thao tác cấp số sẽ bị từ chối (409
                      PATIENT_NOT_PRESENT) thay vì cấp một số “chưa đến”.
                    </span>
                  </span>
                </label>
              </div>

              <div className="flex justify-end gap-2 border-t border-border pt-4">
                <Button type="button" variant="ghost" onClick={onClose}>
                  Đóng
                </Button>
                <Button type="submit" disabled={enrollMutation.isPending || !ctx}>
                  {enrollMutation.isPending ? (
                    <Spinner className="mr-1" />
                  ) : (
                    <CheckCircle2 className="mr-1 size-4" aria-hidden="true" />
                  )}
                  Cấp số & điều phối
                </Button>
              </div>
            </form>
          </div>
        )}
      </div>
    </Modal>
  );
}
