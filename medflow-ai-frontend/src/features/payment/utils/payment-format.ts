import type { ChargeDataStatus, PaymentStatus, SettlementStatus } from '../types/payment.types';

/** Backend tự đánh EXPIRED sau 15 phút (ADR 0042). */
export const PAYMENT_QR_TTL_MS = 15 * 60 * 1000;
export const PAYMENT_POLL_INTERVAL_MS = 5_000;

const SETTLEMENT_LABELS: Record<SettlementStatus, string> = {
  UNPAID: 'Chưa thanh toán',
  PAID: 'Đã thanh toán',
  MISMATCH: 'Thanh toán không khớp',
  UNDETERMINED: 'Chưa xác định',
};

const PAYMENT_LABELS: Record<PaymentStatus, string> = {
  UNPAID: 'Chưa thanh toán',
  PENDING: 'Chờ thanh toán',
  PAID: 'Đã thanh toán',
  EXPIRED: 'Mã đã hết hạn',
  CANCELLED: 'Đã hủy',
  FAILED: 'Thanh toán thất bại',
};

const CHARGE_STATUS_LABELS: Record<ChargeDataStatus, string> = {
  VALID: '',
  INVALID: 'Dữ liệu phí không hợp lệ',
  UNPRICED: 'Chưa có giá',
};

const vndFormatter = new Intl.NumberFormat('vi-VN');

/**
 * Định dạng tiền tệ VNĐ thống nhất cho phân hệ thanh toán.
 * Phân biệt rõ `null`/`undefined` (`'Chưa xác định'`) với `0` (`'0 VNĐ'`) theo ADR-0040.
 */
export function formatVnd(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return 'Chưa xác định';
  }
  return `${vndFormatter.format(value)} VNĐ`;
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const pad = (part: number) => String(part).padStart(2, '0');
  return `${pad(date.getHours())}:${pad(date.getMinutes())} ${pad(date.getDate())}/${pad(
    date.getMonth() + 1,
  )}/${date.getFullYear()}`;
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

export function settlementLabel(status: string | null | undefined): string {
  if (!status) return 'Chưa xác định';
  return SETTLEMENT_LABELS[status as SettlementStatus] ?? status;
}

export function paymentLabel(status: string | null | undefined): string {
  if (!status) return 'Chưa thanh toán';
  return PAYMENT_LABELS[status as PaymentStatus] ?? status;
}

export function chargeStatusLabel(status: ChargeDataStatus): string {
  return CHARGE_STATUS_LABELS[status] ?? '';
}
