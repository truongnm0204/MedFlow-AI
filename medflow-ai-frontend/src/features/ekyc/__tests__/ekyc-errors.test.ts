import { describe, expect, it } from 'vitest';

import type { EkycFailureCode, EkycVerificationStatus } from '../types/ekyc.types';
import {
  EKYC_FAILURE_MESSAGES,
  getEkycFailureMessage,
  isEkycTerminal,
  isRetriableFailure,
} from '../utils/ekyc-errors';

/**
 * Runtime mirror of the `EkycFailureCode` union. The compiler fails this assignment
 * if the union ever grows without the list being updated (see the assignment test).
 */
const ALL_FAILURE_CODES: readonly EkycFailureCode[] = [
  'SIGNATURE_INVALID',
  'MISSING_STEP',
  'EVIDENCE_MISSING',
  'INVALID_BINDING',
  'UNVERIFIABLE_BINDING',
  'LIVENESS_FAILED',
  'COMPARE_BELOW_THRESHOLD',
  'TAMPERING_DETECTED',
  'OCR_MISMATCH',
  'SESSION_EXPIRED',
];

const RETRIABLE_CODES: readonly EkycFailureCode[] = [
  'LIVENESS_FAILED',
  'COMPARE_BELOW_THRESHOLD',
  'MISSING_STEP',
  'EVIDENCE_MISSING',
  'SESSION_EXPIRED',
];

const NON_RETRIABLE_CODES: readonly EkycFailureCode[] = [
  'SIGNATURE_INVALID',
  'INVALID_BINDING',
  'UNVERIFIABLE_BINDING',
  'TAMPERING_DETECTED',
  'OCR_MISMATCH',
];

const ALL_STATUSES: readonly EkycVerificationStatus[] = [
  'INITIATED',
  'DOCUMENT_FRONT_DONE',
  'DOCUMENT_BACK_DONE',
  'FACE_DONE',
  'FINALIZED',
  'VERIFIED',
  'REJECTED',
  'EXPIRED',
  'CONSUMED',
];

const TERMINAL_STATUSES: readonly EkycVerificationStatus[] = [
  'VERIFIED',
  'REJECTED',
  'EXPIRED',
  'CONSUMED',
];

const GENERIC_FAILURE_MESSAGE = 'Xác minh danh tính không thành công. Vui lòng thử lại.';

describe('EKYC_FAILURE_MESSAGES', () => {
  it('có đúng một thông điệp cho mỗi EkycFailureCode (guard đầy đủ)', () => {
    expect(Object.keys(EKYC_FAILURE_MESSAGES).sort()).toEqual([...ALL_FAILURE_CODES].sort());
  });

  it('mọi thông điệp đều là chuỗi tiếng Việt không rỗng và không trùng nhau', () => {
    for (const code of ALL_FAILURE_CODES) {
      const message = EKYC_FAILURE_MESSAGES[code];
      expect(typeof message, code).toBe('string');
      expect(message.trim(), code).not.toBe('');
      // Thông điệp phải là câu hướng dẫn tiếng Việt, không phải mã lỗi thô.
      expect(message, code).not.toBe(code);
    }

    const uniqueMessages = new Set(ALL_FAILURE_CODES.map((code) => EKYC_FAILURE_MESSAGES[code]));
    expect(uniqueMessages.size).toBe(ALL_FAILURE_CODES.length);
  });

  it('giữ nguyên nội dung hướng dẫn người dùng cho các mã quan trọng', () => {
    expect(EKYC_FAILURE_MESSAGES.LIVENESS_FAILED).toBe(
      'Không thể xác minh người thật. Vui lòng thử lại ở nơi đủ ánh sáng và giữ khuôn mặt rõ ràng trong khung hình.',
    );
    expect(EKYC_FAILURE_MESSAGES.SESSION_EXPIRED).toBe(
      'Phiên xác minh đã hết hạn. Vui lòng bắt đầu lại quá trình xác minh.',
    );
    expect(EKYC_FAILURE_MESSAGES.OCR_MISMATCH).toBe(
      'Thông tin trên giấy tờ không khớp với thông tin đăng ký. Vui lòng kiểm tra lại thông tin cá nhân.',
    );
  });
});

describe('getEkycFailureMessage', () => {
  it('trả về đúng thông điệp đã ánh xạ cho từng mã lỗi', () => {
    for (const code of ALL_FAILURE_CODES) {
      expect(getEkycFailureMessage(code), code).toBe(EKYC_FAILURE_MESSAGES[code]);
    }
  });

  it('trả về fallback chung khi mã lỗi là null hoặc undefined', () => {
    expect(getEkycFailureMessage(null)).toBe(GENERIC_FAILURE_MESSAGE);
    expect(getEkycFailureMessage(undefined)).toBe(GENERIC_FAILURE_MESSAGE);
  });

  it('ưu tiên fallback tùy biến khi không có mã lỗi', () => {
    expect(getEkycFailureMessage(null, 'Lỗi tùy biến')).toBe('Lỗi tùy biến');
    expect(getEkycFailureMessage(undefined, 'Lỗi tùy biến')).toBe('Lỗi tùy biến');
  });

  it('không dùng fallback khi mã lỗi đã có thông điệp', () => {
    expect(getEkycFailureMessage('TAMPERING_DETECTED', 'Lỗi tùy biến')).toBe(
      EKYC_FAILURE_MESSAGES.TAMPERING_DETECTED,
    );
  });

  it('dùng fallback khi nhận mã lạ ngoài union (dữ liệu backend mới hơn)', () => {
    expect(getEkycFailureMessage('FUTURE_CODE' as EkycFailureCode, 'Lỗi tùy biến')).toBe(
      'Lỗi tùy biến',
    );
  });
});

describe('isEkycTerminal', () => {
  it('trả về true cho các trạng thái kết thúc', () => {
    for (const status of TERMINAL_STATUSES) {
      expect(isEkycTerminal(status), status).toBe(true);
    }
  });

  it('trả về false cho các trạng thái còn tiến triển được', () => {
    for (const status of ALL_STATUSES) {
      if (!TERMINAL_STATUSES.includes(status)) {
        expect(isEkycTerminal(status), status).toBe(false);
      }
    }
  });
});

describe('isRetriableFailure', () => {
  it('trả về true cho các mã lỗi có thể thử lại', () => {
    for (const code of RETRIABLE_CODES) {
      expect(isRetriableFailure(code), code).toBe(true);
    }
  });

  it('trả về false cho các mã lỗi cần hỗ trợ hoặc dữ liệu không thể cứu', () => {
    for (const code of NON_RETRIABLE_CODES) {
      expect(isRetriableFailure(code), code).toBe(false);
    }
  });

  it('trả về false khi không có mã lỗi', () => {
    expect(isRetriableFailure(null)).toBe(false);
    expect(isRetriableFailure(undefined)).toBe(false);
  });

  it('phân loại toàn bộ union mà không bỏ sót mã nào', () => {
    for (const code of ALL_FAILURE_CODES) {
      const expected = RETRIABLE_CODES.includes(code);
      expect(isRetriableFailure(code), code).toBe(expected);
    }
  });
});
