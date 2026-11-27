import { AxiosError } from 'axios';

import { getErrorMessage } from '@/shared/lib/get-error-message';

/**
 * Backend encodes a stable code as a `[CODE]` prefix inside ProblemDetails text
 * (e.g. `[QUEUE_ENROLL_REQUIRED] Tài khoản nhân sự chưa được cấp quyền...`).
 * A future backend may send an explicit `code`/`errorCode` field; both are honoured.
 */
const BRACKETED_CODE = /\[([A-Z][A-Z0-9_]{2,})\]/;

export const COORDINATION_ERROR_MESSAGES = {
  CLINICAL_HANDOFF_REQUIRED:
    'Phiên khám lâm sàng trước chưa được giải phóng. Cần xác nhận phiên bị gián đoạn trước khi điều phối lại.',
  PATIENT_NOT_PRESENT: 'Chưa xác nhận bệnh nhân có mặt tại quầy.',
  ACTIVE_QUEUE_ALREADY_EXISTS:
    'Bệnh nhân vẫn còn một lượt đang phục vụ tại bệnh viện này. Xử lý lượt hiện tại trước khi cấp lượt mới.',
  QUEUE_ENROLL_REQUIRED:
    'Tài khoản chưa được cấp quyền đăng ký & điều phối lấy số hàng đợi (QUEUE_ENROLL).',
  RECOVERY_REQUEST_ALREADY_OPEN: 'Đã có một yêu cầu phục hồi đang mở cho lượt khám này.',
  RECOVERY_REQUEST_ALREADY_UNRESOLVED:
    'Đã có một yêu cầu phục hồi chưa kết thúc trên vé này. Xử lý yêu cầu hiện tại trước khi tạo thêm.',
  RECOVERY_NOT_CLINICAL_QUEUE:
    'Vé này không phải vé khám lâm sàng (EXAMINATION / RETURN_CONSULTATION) nên không thể phục hồi.',
  RECOVERY_SESSION_NOT_INTERRUPTED:
    'Phiên khám không ở trạng thái gián đoạn nên không thể phục hồi.',
  RECOVERY_STALE_STATE:
    'Trạng thái hàng đợi đã thay đổi. Tải lại ngữ cảnh phục hồi rồi thao tác lại.',
  RECOVERY_GRANT_EXPIRED:
    'Uỷ quyền phục hồi đã hết hạn (2 giờ). Cần cấp uỷ quyền mới trước khi xác nhận.',
  RECOVERY_GRANT_NOT_ACTIVE:
    'Uỷ quyền phục hồi không còn hiệu lực (đã thu hồi, đã dùng hoặc đã hết hạn).',
  RECOVERY_VISIT_TERMINAL: 'Lượt khám đã ở trạng thái kết thúc nên không thể phục hồi phiên.',
  RECOVERY_IDEMPOTENCY_MISMATCH:
    'Yêu cầu trùng khoá idempotency nhưng nội dung khác. Không thực hiện lại — kiểm tra trạng thái hiện tại.',
  RECOVERY_ALREADY_RECOVERED: 'Yêu cầu phục hồi này đã được xác nhận phục hồi trước đó.',
  RECOVERY_ATTESTATION_REQUIRED:
    'Cần xác nhận tường minh rằng phiên khám đã thực sự bị gián đoạn (ConfirmInterrupted).',
  RECOVERY_SELF_GRANT_FORBIDDEN: 'Không thể tự cấp uỷ quyền phục hồi cho chính mình.',
  RECOVERY_RECIPIENT_NOT_ELIGIBLE:
    'Bác sĩ nhận uỷ quyền không đủ điều kiện (cùng bệnh viện, cùng chuyên khoa, có quyền phục hồi).',
  RECOVERY_ISSUER_NOT_ELIGIBLE:
    'Tài khoản không đủ điều kiện cấp uỷ quyền phục hồi cho lượt khám này.',
} as const;

export type CoordinationErrorCode = keyof typeof COORDINATION_ERROR_MESSAGES;

/**
 * D6: 404 means the actor cannot see the resource (missing, other hospital, out of scope) — the UI
 * must treat all three the same and never echo a server sentence that hints which one it was.
 */
export const COORDINATION_NOT_FOUND_MESSAGE = 'Không tìm thấy lượt khám hoặc phiên khám phù hợp.';

/** D6: 403 means the resource is visible but the actor lacks the permission for this action. */
export const COORDINATION_FORBIDDEN_MESSAGE =
  'Bạn không đủ quyền thực hiện thao tác này trên lượt khám.';

/** Extract the stable error code from an Axios error, or `null` when the response carries none. */
export function extractProblemCode(error: unknown): string | null {
  if (!(error instanceof AxiosError)) return null;
  const data = error.response?.data;
  if (!data || typeof data !== 'object' || Array.isArray(data)) return null;

  const record = data as Record<string, unknown>;
  for (const key of ['code', 'errorCode']) {
    const explicit = record[key];
    if (typeof explicit === 'string' && explicit.trim()) return explicit.trim().toUpperCase();
  }

  for (const key of ['detail', 'error', 'message', 'title']) {
    const text = record[key];
    const match = typeof text === 'string' ? text.match(BRACKETED_CODE) : null;
    if (match) return match[1];
  }

  return null;
}

/**
 * Message for return-coordination / recovery actions.
 *
 * Known coordination codes get one canonical sentence so the UI never paraphrases a
 * compliance-relevant outcome. Everything else falls back to the shared ProblemDetails
 * handling (server text, 429 handling, network errors).
 */
function isCoordinationErrorCode(code: string): code is CoordinationErrorCode {
  return code in COORDINATION_ERROR_MESSAGES;
}

export function getCoordinationErrorMessage(error: unknown, fallback?: string): string {
  if (error instanceof AxiosError) {
    const code = extractProblemCode(error);
    if (code && isCoordinationErrorCode(code)) return COORDINATION_ERROR_MESSAGES[code];

    if (error.response?.status === 404) return COORDINATION_NOT_FOUND_MESSAGE;
    if (error.response?.status === 403) return COORDINATION_FORBIDDEN_MESSAGE;
  }
  return getErrorMessage(error, fallback);
}
