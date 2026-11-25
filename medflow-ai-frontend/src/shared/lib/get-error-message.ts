import { AxiosError } from 'axios';

import { parseRetryAfterMs } from './axios';

interface ProblemDetails {
  title?: string;
  detail?: string;
  error?: string;
  message?: string;
  errors?: Record<string, string[]>;
}

function extractRetryAfterSeconds(error: AxiosError): number | null {
  const headers = error.response?.headers as Record<string, unknown> | undefined;
  const raw = headers?.['retry-after'] ?? headers?.['Retry-After'];
  const ms = parseRetryAfterMs(raw);
  if (ms === null || ms <= 0) return null;
  return Math.max(1, Math.ceil(ms / 1_000));
}

/** Rút message hiển thị từ axios error (backend trả ProblemDetails). */
export function getErrorMessage(
  error: unknown,
  fallback = 'Đã có lỗi xảy ra. Vui lòng thử lại.',
): string {
  if (error instanceof AxiosError) {
    if (error.response?.status === 429) {
      const retryAfterSeconds = extractRetryAfterSeconds(error);
      if (retryAfterSeconds !== null) {
        return `Bạn đã thực hiện quá nhiều yêu cầu. Vui lòng thử lại sau ${retryAfterSeconds} giây.`;
      }
    }

    const data = error.response?.data as ProblemDetails | string | undefined;
    if (typeof data === 'string' && data.length < 200) return data;
    if (data && typeof data === 'object') {
      if (data.detail) return data.detail;
      if (data.error) return data.error;
      if (data.message) return data.message;
      if (data.title) return data.title;
      const first = data.errors && Object.values(data.errors)[0]?.[0];
      if (first) return first;
    }
    if (error.code === 'ERR_NETWORK')
      return 'Không kết nối được máy chủ. Kiểm tra backend đã chạy chưa.';
    if (error.response?.status === 429)
      return 'Bạn đã thực hiện quá nhiều yêu cầu. Vui lòng thử lại sau.';
  }
  if (error instanceof AggregateError)
    return 'Không kết nối được máy chủ. Kiểm tra backend đã chạy chưa.';
  return error instanceof Error ? error.message : fallback;
}
