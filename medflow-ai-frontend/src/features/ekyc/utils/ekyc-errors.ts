import type { EkycFailureCode, EkycVerificationStatus } from '../types/ekyc.types';
import { EKYC_TERMINAL_STATUSES } from '../types/ekyc.types';

/**
 * Coarse, user-safe Vietnamese guidance per backend failure code.
 * Never includes evidence payloads, OCR PII, or internal diagnostics.
 */
export const EKYC_FAILURE_MESSAGES: Record<EkycFailureCode, string> = {
  SIGNATURE_INVALID:
    'Dữ liệu xác minh không hợp lệ. Vui lòng liên hệ bộ phận hỗ trợ để được kiểm tra lại.',
  MISSING_STEP:
    'Bạn chưa hoàn thành đầy đủ các bước xác minh. Vui lòng thực hiện lại các bước còn thiếu.',
  EVIDENCE_MISSING:
    'Hình ảnh xác minh chưa được ghi nhận đầy đủ. Vui lòng chụp lại giấy tờ và khuôn mặt.',
  INVALID_BINDING:
    'Dữ liệu xác minh chưa nhất quán. Vui lòng chụp lại giấy tờ và khuôn mặt trong cùng một phiên.',
  UNVERIFIABLE_BINDING:
    'Không thể đối chiếu dữ liệu xác minh. Vui lòng chụp lại giấy tờ và khuôn mặt trong điều kiện rõ ràng hơn.',
  LIVENESS_FAILED:
    'Không thể xác minh người thật. Vui lòng thử lại ở nơi đủ ánh sáng và giữ khuôn mặt rõ ràng trong khung hình.',
  COMPARE_BELOW_THRESHOLD:
    'Khuôn mặt không khớp với ảnh trên giấy tờ. Vui lòng thử lại ở nơi đủ ánh sáng và giữ khuôn mặt rõ ràng.',
  TAMPERING_DETECTED:
    'Giấy tờ tùy thân không hợp lệ hoặc có dấu hiệu chỉnh sửa. Vui lòng kiểm tra lại giấy tờ và thử lại.',
  OCR_MISMATCH:
    'Thông tin trên giấy tờ không khớp với thông tin đăng ký. Vui lòng kiểm tra lại thông tin cá nhân.',
  SESSION_EXPIRED: 'Phiên xác minh đã hết hạn. Vui lòng bắt đầu lại quá trình xác minh.',
};

const GENERIC_FAILURE_MESSAGE = 'Xác minh danh tính không thành công. Vui lòng thử lại.';

const RETRIABLE_FAILURE_CODES: readonly EkycFailureCode[] = [
  'LIVENESS_FAILED',
  'COMPARE_BELOW_THRESHOLD',
  'MISSING_STEP',
  'EVIDENCE_MISSING',
  'SESSION_EXPIRED',
] as const;

export function getEkycFailureMessage(
  code: EkycFailureCode | null | undefined,
  fallback?: string,
): string {
  if (code) {
    const message = EKYC_FAILURE_MESSAGES[code];
    if (message) {
      return message;
    }
  }

  return fallback ?? GENERIC_FAILURE_MESSAGE;
}

export function isEkycTerminal(status: EkycVerificationStatus): boolean {
  return EKYC_TERMINAL_STATUSES.includes(status);
}

export function isRetriableFailure(code: EkycFailureCode | null | undefined): boolean {
  if (!code) {
    return false;
  }

  return RETRIABLE_FAILURE_CODES.includes(code);
}
