/**
 * eKYC types — mirror backend DTOs exactly (medflow-ai-backend .../DTOs/Ekyc/EkycSessionDtos.cs)
 * and domain constants (.../Domain/Constants/EkycConstants.cs). Do NOT invent fields:
 * the backend is the source of truth; these shapes were read from feat/ekyc-phase1-backend
 * post-slice-5 (owner scope: CCCD-only VERIFIED, no face step).
 *
 * JSON serialization is camelCase (ASP.NET default for record positional properties).
 */

/** POST /api/v1/ekyc/sessions request body. Consent is mandatory before any image upload. */
export interface CreateEkycSessionRequest {
  /** Consent text version the user accepted — must match the server's active configured version. */
  consentVersion: string;
  /** Explicit acceptance flag — must be true. */
  consentAccepted: boolean;
  /** Optional declared identity card number (CCCD) — cross-checked against OCR at finalize. */
  identityCard?: string;
}

/**
 * POST /api/v1/reception/ekyc/patients/{patientId}/sessions request body (reception-assisted).
 * patientId is supplied via the route; the body carries the rest.
 */
export interface CreateReceptionEkycSessionRequest {
  /** Patient profile id — set from the route, not typed by the user. */
  patientId: number;
  consentVersion: string;
  consentAccepted: boolean;
  identityCard?: string;
}

/**
 * POST /api/v1/ekyc/sessions (or reception equivalent) → 201 response.
 * proxyBasePath already embeds the session id: "/api/v1/ekyc/proxy/{sessionId}".
 */
export interface CreateEkycSessionResponse {
  sessionId: string;
  proxyBasePath: string;
  status: EkycVerificationStatus;
  /** ISO-8601 date-time string (backend serializes DateTime). */
  expiresAt: string;
}

/** GET .../sessions/{id} → 200 response. Never leaks evidence payloads or OCR PII. */
export interface EkycSessionStatusResponse {
  sessionId: string;
  status: EkycVerificationStatus;
  verificationMode: EkycVerificationMode;
  purpose: EkycPurpose;
  bindingState: EkycBindingState;
  attemptCount: number;
  /** ISO-8601 date-time string. */
  expiresAt: string;
  expired: boolean;
  failureReason: string | null;
  /** ISO-8601 date-time string. */
  createdAt: string;
}

/**
 * POST .../sessions/{id}/finalize → 200 response.
 * Verdict is fully backend-decided; the client never submits verdicts.
 */
export interface FinalizeEkycSessionResponse {
  sessionId: string;
  status: EkycVerificationStatus;
  failureReason: string | null;
  failureCode: EkycFailureCode | null;
  /** ISO-8601 date-time string, set exactly once on single-use consumption. */
  consumedAt: string | null;
  accountActivated?: boolean | null;
  /** Dedupe matrix outcome code (EkycDedupeOutcomes) — route the user by this. */
  activationOutcome?: EkycActivationOutcome | string | null;
  activationReviewReason?: string | null;
  purpose?: EkycPurpose | null;
  patientId?: number | null;
  patientIdentityStatus?: PatientIdentityStatus | null;
}

/** POST /api/v1/reception/ekyc/patients/unverified request body. */
export interface CreateUnverifiedPatientProfileRequest {
  fullName?: string;
  /** ISO date yyyy-MM-dd (DateOnly). */
  dateOfBirth?: string;
  gender?: string;
  phone?: string;
  address?: string;
  email?: string;
  identityCard?: string;
  /** One of PatientUnverifiedReasons — mandatory. */
  reason: PatientUnverifiedReason;
  /** Optional free-text operational note. */
  note?: string;
}

/** POST /api/v1/reception/ekyc/patients/unverified → 201 response. */
export interface CreateUnverifiedPatientProfileResponse {
  patientId: number;
  patientCode: string;
  fullName: string;
  identityStatus: PatientIdentityStatus;
  unverifiedReason: PatientUnverifiedReason;
}

/** Session state machine values (EkycVerificationStatuses). */
export type EkycVerificationStatus =
  | 'INITIATED'
  | 'DOCUMENT_FRONT_DONE'
  | 'DOCUMENT_BACK_DONE'
  | 'FACE_DONE'
  | 'FINALIZED'
  | 'VERIFIED'
  | 'REJECTED'
  | 'EXPIRED'
  | 'CONSUMED';

/** Terminal states — session can no longer be advanced. */
export const EKYC_TERMINAL_STATUSES: readonly EkycVerificationStatus[] = [
  'VERIFIED',
  'REJECTED',
  'EXPIRED',
  'CONSUMED',
] as const;

/** Who drives the capture (EkycVerificationModes). */
export type EkycVerificationMode = 'SELF_SERVICE' | 'RECEPTION_ASSISTED';

/** What the verification is for (EkycPurposes). */
export type EkycPurpose = 'ACCOUNT_ACTIVATION' | 'PATIENT_PROFILE';

/** Step-binding validation states (fail-closed). NOT_APPLICABLE in document-only flow (no compare). */
export type EkycBindingState =
  | 'PENDING'
  | 'NOT_APPLICABLE'
  | 'VALID'
  | 'INVALID_BINDING'
  | 'UNVERIFIABLE_BINDING';

/** Patient identity verification state (PatientIdentityStatuses). VERIFIED only via engine finalize. */
export type PatientIdentityStatus = 'VERIFIED' | 'UNVERIFIED';

/** Owner-approved reasons for creating a profile without eKYC (PatientUnverifiedReasons). */
export type PatientUnverifiedReason =
  | 'CHILD'
  | 'FOREIGNER'
  | 'NO_VALID_DOCUMENT'
  | 'CONSENT_DECLINED'
  | 'VNPT_UNAVAILABLE'
  | 'QUOTA_EXHAUSTED';

export const PATIENT_UNVERIFIED_REASONS: readonly PatientUnverifiedReason[] = [
  'CHILD',
  'FOREIGNER',
  'NO_VALID_DOCUMENT',
  'CONSENT_DECLINED',
  'VNPT_UNAVAILABLE',
  'QUOTA_EXHAUSTED',
] as const;

/** Reasons that require a declared identification document number (per backend). */
export const PATIENT_UNVERIFIED_REASONS_REQUIRING_IDENTIFICATION: readonly PatientUnverifiedReason[] =
  ['CONSENT_DECLINED', 'VNPT_UNAVAILABLE', 'QUOTA_EXHAUSTED'] as const;

/** Dedupe matrix outcome codes (EkycDedupeOutcomes) — surfaced in finalize response. */
export type EkycActivationOutcome =
  | 'NEW_IDENTITY_ACTIVATED'
  | 'CLAIM_LINKED_ACTIVATED'
  | 'FAMILY_MANAGED_PENDING_REVIEW'
  | 'CONFLICT_ACCOUNT_EXISTS'
  | 'INFO_MISMATCH_PENDING_REVIEW'
  | 'MULTIPLE_RECORDS_CONFLICT'
  | 'NOT_APPLICABLE';

/**
 * Coarse machine-readable rejection codes emitted by strict finalize
 * (read from EkycVerificationService). Never evidence payloads or OCR PII.
 */
export type EkycFailureCode =
  | 'SIGNATURE_INVALID'
  | 'MISSING_STEP'
  | 'EVIDENCE_MISSING'
  | 'INVALID_BINDING'
  | 'UNVERIFIABLE_BINDING'
  | 'LIVENESS_FAILED'
  | 'COMPARE_BELOW_THRESHOLD'
  | 'TAMPERING_DETECTED'
  | 'OCR_MISMATCH'
  | 'SESSION_EXPIRED';
