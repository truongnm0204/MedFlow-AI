import { AxiosError, AxiosHeaders } from 'axios';
import { describe, expect, it } from 'vitest';

import { getErrorMessage } from '../get-error-message';
import { queryClient, shouldRetryQuery } from '../query-client';

function makeAxiosError(
  status: number,
  data?: unknown,
  headers?: Record<string, string>,
): AxiosError {
  const axiosHeaders = new AxiosHeaders(headers);
  const config = { headers: axiosHeaders };
  return new AxiosError(
    `Request failed with status code ${status}`,
    'ERR_BAD_REQUEST',
    config,
    undefined,
    {
      status,
      statusText: String(status),
      headers: axiosHeaders,
      config,
      data,
    },
  );
}

describe('getErrorMessage — HTTP 429 & ProblemDetails', () => {
  it('formats wait duration in seconds when 429 includes Retry-After header', () => {
    const err = makeAxiosError(
      429,
      {
        status: 429,
        title: 'Quá giới hạn truy cập',
        detail: 'Bạn thao tác quá nhanh. Vui lòng thử lại sau.',
      },
      { 'retry-after': '25' },
    );

    expect(getErrorMessage(err)).toBe(
      'Bạn đã thực hiện quá nhiều yêu cầu. Vui lòng thử lại sau 25 giây.',
    );
  });

  it('falls back to ProblemDetails.detail when 429 omits Retry-After header', () => {
    const err = makeAxiosError(429, {
      status: 429,
      title: 'Quá giới hạn truy cập',
      detail: 'Bạn đã thực hiện quá nhiều yêu cầu. Vui lòng thử lại sau 15 giây.',
    });

    expect(getErrorMessage(err)).toBe(
      'Bạn đã thực hiện quá nhiều yêu cầu. Vui lòng thử lại sau 15 giây.',
    );
  });

  it('returns Vietnamese rate-limit message when 429 has neither Retry-After nor ProblemDetails', () => {
    const err = makeAxiosError(429, undefined);

    expect(getErrorMessage(err)).toBe('Bạn đã thực hiện quá nhiều yêu cầu. Vui lòng thử lại sau.');
  });

  it('preserves ProblemDetails.detail for non-429 domain errors', () => {
    const err = makeAxiosError(409, {
      status: 409,
      title: 'Xung đột dữ liệu',
      detail: 'Khung giờ khám vừa hết chỗ.',
    });

    expect(getErrorMessage(err)).toBe('Khung giờ khám vừa hết chỗ.');
  });
});

describe('queryClient — retry policy', () => {
  it('disables automatic retries on mutations so POST/PATCH business actions are not replayed', () => {
    expect(queryClient.getDefaultOptions().mutations?.retry).toBe(false);
  });

  it('does not retry queries on 429, 401, 403, or 404, while allowing 1 retry on transient 500', () => {
    expect(shouldRetryQuery(0, makeAxiosError(429))).toBe(false);
    expect(shouldRetryQuery(0, makeAxiosError(401))).toBe(false);
    expect(shouldRetryQuery(0, makeAxiosError(403))).toBe(false);
    expect(shouldRetryQuery(0, makeAxiosError(404))).toBe(false);
    expect(shouldRetryQuery(0, makeAxiosError(500))).toBe(true);
    expect(shouldRetryQuery(1, makeAxiosError(500))).toBe(false);
  });
});
