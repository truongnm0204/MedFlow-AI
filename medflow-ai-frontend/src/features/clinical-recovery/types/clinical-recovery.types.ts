/**
 * Stage E2 — Clinical Session Recovery (F01 Option B).
 *
 * Locked to `docs/decisions/clinical-session-recovery-api-contract.md` and the Commit A DTOs
 * (`MedFlow.Application/DTOs/Visits/ClinicalSessionRecoveryDtos.cs`). Field names mirror the
 * camelCase JSON the API returns; do not rename without changing the backend contract.
 */

export const CLINICAL_RECOVERY_STATUSES = [
  'REQUESTED',
  'GRANTED',
  'RECOVERED',
  'REJECTED',
  'REVOKED',
  'EXPIRED',
  'CANCELLED',
] as const;

export type ClinicalRecoveryStatus = (typeof CLINICAL_RECOVERY_STATUSES)[number];

export interface ClinicalRecoveryStatusMeta {
  label: string;
  tone: 'sky' | 'emerald' | 'amber' | 'rose' | 'slate';
}

export const CLINICAL_RECOVERY_STATUS_META: Readonly<
  Record<ClinicalRecoveryStatus, ClinicalRecoveryStatusMeta>
> = {
  REQUESTED: { label: 'Chờ cấp quyền phục hồi', tone: 'amber' },
  GRANTED: { label: 'Đã cấp quyền, chờ xác nhận', tone: 'sky' },
  RECOVERED: { label: 'Đã phục hồi phiên khám', tone: 'emerald' },
  REJECTED: { label: 'Bị từ chối', tone: 'rose' },
  REVOKED: { label: 'Đã thu hồi quyền', tone: 'rose' },
  EXPIRED: { label: 'Quyền đã hết hạn', tone: 'slate' },
  CANCELLED: { label: 'Đã rút yêu cầu', tone: 'slate' },
};

/** Terminal states are never reactivated (§5.2). */
export const TERMINAL_CLINICAL_RECOVERY_STATUSES: readonly ClinicalRecoveryStatus[] = [
  'RECOVERED',
  'REJECTED',
  'REVOKED',
  'EXPIRED',
  'CANCELLED',
];

export interface ClinicalRecoveryQueueDto {
  queueEntryId: number;
  queueType: string;
  status: string;
  stateVersion: number;
  queueDate: string | null;
  completedAt: string | null;
}

/**
 * Deliberately carries no reason text: reasons live in `audit_logs` only and never travel through
 * API or SignalR payloads (decision D7).
 */
export interface ClinicalRecoveryRequestDto {
  requestId: number;
  status: ClinicalRecoveryStatus;
  hospitalId: number;
  visitId: number;
  queueEntryId: number;
  originalDoctorId: number | null;
  authorizedDoctorId: number | null;
  ownSession: boolean;
  expectedQueueStateVersion: number;
  requestedByUserId: number;
  requestedAt: string;
  grantedAt: string | null;
  expiresAt: string | null;
  recoveredAt: string | null;
  revokedAt: string | null;
  rejectedAt: string | null;
  cancelledAt: string | null;
  expiredAt: string | null;
}

/** Server-computed authorization surface — the UI gates actions on these flags, not on roles. */
export interface ClinicalRecoveryCapabilitiesDto {
  canRequest: boolean;
  canGrant: boolean;
  canConfirmOwnSession: boolean;
  canConfirmDelegated: boolean;
  canReject: boolean;
  canRevoke: boolean;
  canCancel: boolean;
}

export interface ClinicalRecoveryContextDto {
  visitId: number;
  visitCode: string;
  visitStatus: string;
  hospitalId: number | null;
  assignedDoctorId: number | null;
  assignedDoctorCode: string | null;
  assignedDoctorName: string | null;
  queues: ClinicalRecoveryQueueDto[];
  requests: ClinicalRecoveryRequestDto[];
  capabilities: ClinicalRecoveryCapabilitiesDto;
}

export interface ClinicalRecoveryConfirmResponseDto {
  requestId: number;
  status: ClinicalRecoveryStatus;
  visitId: number;
  visitCode: string;
  queueEntryId: number;
  queueStatus: string;
  queueStateVersion: number;
  visitStatus: string;
  recoveredAt: string;
  isIdempotentReplay: boolean;
}

export interface CreateClinicalRecoveryRequestPayload {
  queueEntryId: number;
  expectedStateVersion: number;
  reason: string;
}

export interface GrantClinicalRecoveryRequestPayload {
  authorizedDoctorId: number;
  expectedStateVersion: number;
  reason: string;
}

export interface ClinicalRecoveryDecisionPayload {
  reason: string;
  expectedStateVersion?: number;
}

export interface ConfirmClinicalRecoveryRequestPayload {
  expectedStateVersion: number;
  confirmInterrupted: boolean;
  reason: string;
  idempotencyKey?: string;
}
