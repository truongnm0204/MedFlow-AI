/**
 * eKYC types — mirror backend DTOs exactly (medflow-ai-backend .../DTOs/Ekyc/EkycSessionDtos.cs)
 * and domain constants (.../Domain/Constants/EkycConstants.cs). Do NOT invent fields:
 * the backend is the source of truth; these shapes were read from feat/ekyc-phase1-backend.
 */

/** POST /api/v1/ekyc/sessions request body. Consent is mandatory before any image upload. */
export interface CreateEkycSessionRequest {
  /** Consent text version the user accepted — must match the server's active configured version. */
  consentVersion: string;
  /** Explicit acceptance flag — must be true. */
  consentAccepted: boolean;
}

/**
 * POST /api/v1/ekyc/sessions → 201 response.
 * proxyBasePath already embeds the session id: "/api/v1/ekyc/proxy/{sessionId}".
 */
export interface CreateEkycSessionResponse {
  sessionId: string;
  proxyBasePath: string;
  status: EkycVerificationStatus;
  /** ISO-8601 date-time string (backend serializes DateTime). */
  expiresAt: string;
}

/** GET /api/v1/ekyc/sessions/{id} → 200 response. Never leaks evidence payloads or OCR PII. */
export interface EkycSessionStatusResponse {
  sessionId: string;
  status: EkycVerificationStatus;
  verificationMethod: EkycVerificationMethod;
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
 * POST /api/v1/ekyc/sessions/{id}/finalize → 200 response.
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
  activationOutcome?: string | null;
  activationReviewReason?: string | null;
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

/** Discriminator: online VNPT eKYC vs reception-assisted manual verification. */
export type EkycVerificationMethod = 'EKYC_VNPT' | 'RECEPTION_MANUAL';

/** Step-binding validation states (fail-closed). */
export type EkycBindingState = 'PENDING' | 'VALID' | 'INVALID_BINDING' | 'UNVERIFIABLE_BINDING';

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
