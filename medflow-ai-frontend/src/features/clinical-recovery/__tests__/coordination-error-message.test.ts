import { AxiosError, AxiosHeaders } from 'axios';
import { describe, expect, it } from 'vitest';

import {
  COORDINATION_ERROR_MESSAGES,
  extractProblemCode,
  getCoordinationErrorMessage,
} from '../lib/coordination-error-message';

function makeAxiosError(status: number, data?: unknown): AxiosError {
  const headers = new AxiosHeaders();
  const config = { headers };
  return new AxiosError(
    `Request failed with status code ${status}`,
    'ERR_BAD_REQUEST',
    config,
    undefined,
    { status, statusText: String(status), headers, config, data },
  );
}

describe('extractProblemCode', () => {
  it('reads the [CODE] prefix from ProblemDetails.detail', () => {
    const error = makeAxiosError(409, {
      detail: '[CLINICAL_HANDOFF_REQUIRED] Phiên khám lâm sàng của bệnh nhân vẫn đang mở.',
    });

    expect(extractProblemCode(error)).toBe('CLINICAL_HANDOFF_REQUIRED');
  });

  it('prefers an explicit code field over text prefixes', () => {
    const error = makeAxiosError(409, {
      code: 'patient_not_present',
      detail: '[SOMETHING_ELSE] Chi tiết khác.',
    });

    expect(extractProblemCode(error)).toBe('PATIENT_NOT_PRESENT');
  });

  it('returns null for non-axios errors and code-less payloads', () => {
    expect(extractProblemCode(new Error('boom'))).toBeNull();
    expect(extractProblemCode(makeAxiosError(500, { detail: 'Lỗi máy chủ.' }))).toBeNull();
    expect(extractProblemCode(makeAxiosError(500, 'plain text'))).toBeNull();
  });
});

describe('getCoordinationErrorMessage', () => {
  it('maps known coordination codes to one canonical sentence', () => {
    const error = makeAxiosError(409, {
      detail: '[ACTIVE_QUEUE_ALREADY_EXISTS] Chi tiết từ server.',
    });

    expect(getCoordinationErrorMessage(error)).toBe(
      COORDINATION_ERROR_MESSAGES.ACTIVE_QUEUE_ALREADY_EXISTS,
    );
  });

  it('keeps server text for unknown codes instead of hiding the reason', () => {
    const error = makeAxiosError(409, {
      detail: '[SOMETHING_NEW] Bác sĩ chưa được phân công cho lượt khám này.',
    });

    expect(getCoordinationErrorMessage(error)).toBe(
      '[SOMETHING_NEW] Bác sĩ chưa được phân công cho lượt khám này.',
    );
  });

  it('falls back to the caller fallback when nothing else matches', () => {
    expect(getCoordinationErrorMessage(undefined, 'Không điều phối được lượt khám.')).toBe(
      'Không điều phối được lượt khám.',
    );
  });
});
