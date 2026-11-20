/** Backend tự đánh EXPIRED sau 15 phút (ADR 0042). */
export const PAYMENT_QR_TTL_MS = 15 * 60 * 1000;
export const PAYMENT_POLL_INTERVAL_MS = 5_000;

const vndFormatter = new Intl.NumberFormat('vi-VN');

export function formatVnd(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '—';
  return `${vndFormatter.format(value)} VNĐ`;
}

export function formatCountdown(remainingMs: number): string {
  const totalSeconds = Math.max(0, Math.ceil(remainingMs / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}

export function formatPaidAt(value: string | null | undefined): string {
  if (!value) return '—';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return parsed.toLocaleString('vi-VN', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function formatVisitTime(value: string | null | undefined): string {
  if (!value) return 'Chưa ghi nhận giờ vào';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return parsed.toLocaleString('vi-VN', {
    weekday: 'short',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}
