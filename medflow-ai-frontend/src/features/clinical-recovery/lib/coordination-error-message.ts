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
} as const;

export type CoordinationErrorCode = keyof typeof COORDINATION_ERROR_MESSAGES;

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
export function getCoordinationErrorMessage(error: unknown, fallback?: string): string {
  const code = extractProblemCode(error);
  if (code) {
    const mapped = (COORDINATION_ERROR_MESSAGES as Record<string, string>)[code];
    if (mapped) return mapped;
  }
  return getErrorMessage(error, fallback);
}
