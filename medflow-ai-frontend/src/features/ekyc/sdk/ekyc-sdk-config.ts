/**
 * VNPT eKYC SDK configuration.
 *
 * Integration model (do not change without a backend contract change):
 * the frontend never calls VNPT directly. It embeds the VNPT widget but points the
 * SDK's BACKEND_URL at MedFlow's own backend proxy. The backend returns
 * `proxyBasePath` = "/api/v1/ekyc/proxy/{sessionId}"; the SDK appends inference
 * paths such as "/ai/v1/web/ocr/id" onto BACKEND_URL, so BACKEND_URL must be
 * "{origin}{proxyBasePath}" to produce
 * "/api/v1/ekyc/proxy/{sessionId}/ai/v1/web/ocr/id" — the exact backend proxy route.
 *
 * Credentials: the SDK requires credential headers, but the browser must never hold
 * real VNPT secrets. The backend proxy injects the real credentials server-side, so
 * the frontend passes a deliberate non-secret placeholder in those fields.
 *
 * Results: the frontend never interprets SDK evidence. It is opaque evidence that
 * the backend verifies; the verdict comes only from the finalize endpoint.
 */

import type { VnptEkycConfig, VnptEkycResult } from './vnpt-sdk.types';

/**
 * Mount id required verbatim by the VNPT SDK. The misspelling ("intergrated") is
 * intentional and mandated by the vendor SDK — do not "fix" it.
 */
export const EKYC_SDK_CONTAINER_ID = 'ekyc_sdk_intergrated';

/**
 * Non-secret placeholder substituted for every SDK credential field.
 *
 * The real VNPT credentials are injected by the backend proxy, never exposed to the
 * browser. This value is intentionally not a secret and only satisfies the SDK's
 * non-empty-field validation.
 */
export const VNPT_PLACEHOLDER_CREDENTIAL = 'medflow-proxy-injected';

/**
 * Resolve the default origin. Kept as a tiny helper so the builder stays pure and
 * callable in environments without `window` (node tests) when an explicit origin is
 * supplied.
 */
function resolveDefaultOrigin(): string {
  if (typeof window !== 'undefined' && window.location) {
    return window.location.origin;
  }
  return '';
}

/**
 * Join the app origin and the backend proxy base path with exactly one slash.
 *
 * Pure and side-effect free. `origin` is injectable for testability; it defaults to
 * the current window origin when omitted. Leading/trailing slashes are normalized so
 * the result never contains a double slash and never misses the separator.
 *
 * Example: ("/api/v1/ekyc/proxy/123", "https://app.medflow.vn/")
 *   -> "https://app.medflow.vn/api/v1/ekyc/proxy/123"
 */
export function buildEkycSdkBackendUrl(proxyBasePath: string, origin?: string): string {
  const base = (origin ?? resolveDefaultOrigin()).replace(/\/+$/, '');
  const path = proxyBasePath.replace(/^\/+/, '');
  if (path.length === 0) {
    return base;
  }
  return `${base}/${path}`;
}

/** Options accepted by {@link buildEkycSdkConfig}. */
export interface BuildEkycSdkConfigOptions {
  /** Backend-issued proxy base path, e.g. "/api/v1/ekyc/proxy/{sessionId}". */
  proxyBasePath: string;
  /** App origin override; defaults to window.location.origin. Injectable for tests. */
  origin?: string;
  /** Invoked by the SDK when the whole flow ends. Evidence is opaque. */
  onEndFlow: (result: VnptEkycResult) => void;
  /** Optional per-document callback. Evidence is opaque. */
  onDocumentResult?: (result: VnptEkycResult) => void;
}

/**
 * Build the full VNPT SDK configuration for one eKYC session.
 *
 * Pure: same options produce the same config. Backend routing and placeholder
 * credentials are described in the module header. We render our own wizard shell and
 * result UX, so the SDK's default result screen is disabled. The scanner is the CCDD
 * chip type (9); QR scan is disabled because capture of front/back works without it.
 */
export function buildEkycSdkConfig(options: BuildEkycSdkConfigOptions): VnptEkycConfig {
  const config: VnptEkycConfig = {
    BACKEND_URL: buildEkycSdkBackendUrl(options.proxyBasePath, options.origin),
    TOKEN_KEY: VNPT_PLACEHOLDER_CREDENTIAL,
    TOKEN_ID: VNPT_PLACEHOLDER_CREDENTIAL,
    // Note the vendor's typo: the SDK reads AUTHORIZION, not AUTHORIZATION.
    AUTHORIZION: VNPT_PLACEHOLDER_CREDENTIAL,
    ACCESS_TOKEN: VNPT_PLACEHOLDER_CREDENTIAL,

    // Owner scope 2026-11-28: CCCD-only verification — document capture + document liveness,
    // NO face liveness/compare. The bundle reads FLOW_TAKEN; "DOCUMENT" branches straight to the
    // validations result and skips the face step entirely ("BOTH" would run document then face).
    // SDK_FLOW is the documented name and is kept for compatibility; the bundle ignores it.
    FLOW_TAKEN: 'DOCUMENT',
    SDK_FLOW: 'DOCUMENT_TO_FACE',
    LIST_TYPE_DOCUMENT: [9],
    DOCUMENT_TYPE_START: 9,
    DEFAULT_LANGUAGE: 'vi',

    // QR scan is camera-only friction; front/back capture is sufficient.
    HAS_QR_SCAN: false,
    // Allow both live camera and upload.
    USE_METHOD: 'BOTH',

    // Bundle 3.0 reads these CHECK_*/COMPARE_FACE keys directly. Document-only scope:
    // card liveness stays ON (document authenticity), face/mask/compare are OFF.
    CHECK_LIVENESS_CARD: true,
    CHECK_LIVENESS_FACE: false,
    CHECK_MASKED_FACE: false,
    COMPARE_FACE: false,
    // Documented names kept for compatibility (ignored by the bundle).
    ENABLE_API_LIVENESS_DOCUMENT: true,
    ENABLE_API_LIVENESS_FACE: false,
    ENABLE_API_MASKED_FACE: false,
    ENABLE_API_COMPARE_FACE: false,

    // We render our own shell/result UX instead of the SDK's default result screen.
    HAS_RESULT_SCREEN: false,
    SHOW_STEP: true,

    URL_WEB_OVAL: '/ekyc-sdk/lib/web-oval.json',
    URL_MOBILE_OVAL: '/ekyc-sdk/lib/mobile-oval.json',

    // Results are opaque evidence handed to the caller; verdict is backend-decided.
    CALL_BACK_END_FLOW: options.onEndFlow,
  };

  if (options.onDocumentResult) {
    config.CALL_BACK_DOCUMENT_RESULT = options.onDocumentResult;
  }

  return config;
}
