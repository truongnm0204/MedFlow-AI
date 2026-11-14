import type { ChargeDataStatus, PaymentStatus, SettlementStatus } from '../types/payment.types';

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

/** `null` = backend chưa xác định được số tiền, khác hoàn toàn với 0 đồng. */
export function formatVnd(value: number | null | undefined): string {
  if (value === null || value === undefined) return 'Chưa xác định';
  return `${value.toLocaleString('vi-VN')} đ`;
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
