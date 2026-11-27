/**
 * Stage E2 — Clinical Session Recovery (F01 Option B).
 *
 * Contract types are intentionally minimal: backend Commit A has not landed yet, so only the
 * lifecycle statuses fixed by the approved decision document (§5.2) are defined here.
 * API DTOs land with the backend contract; do not invent field names before it does.
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
