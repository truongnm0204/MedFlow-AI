import { describe, expect, it } from 'vitest';

import {
  chargeStatusLabel,
  formatCountdown,
  formatDateTime,
  formatVnd,
  PAYMENT_POLL_INTERVAL_MS,
  PAYMENT_QR_TTL_MS,
  paymentLabel,
  settlementLabel,
} from '../utils/payment-format';

describe('payment-format unified utilities', () => {
  it('distinguishes null/undefined/NaN from zero amount in formatVnd', () => {
    expect(formatVnd(null)).toBe('Chưa xác định');
    expect(formatVnd(undefined)).toBe('Chưa xác định');
    expect(formatVnd(Number.NaN)).toBe('Chưa xác định');
    expect(formatVnd(0)).toBe('0 VNĐ');
    expect(formatVnd(250_000)).toBe('250.000 VNĐ');
  });

  it('exports QR TTL and poll interval constants', () => {
    expect(PAYMENT_QR_TTL_MS).toBe(15 * 60 * 1000);
    expect(PAYMENT_POLL_INTERVAL_MS).toBe(5_000);
  });

  it('formats countdown and status labels accurately', () => {
    expect(formatCountdown(90_000)).toBe('1:30');
    expect(formatCountdown(-500)).toBe('0:00');
    expect(settlementLabel(null)).toBe('Chưa xác định');
    expect(settlementLabel('PAID')).toBe('Đã thanh toán');
    expect(paymentLabel(undefined)).toBe('Chưa thanh toán');
    expect(paymentLabel('EXPIRED')).toBe('Mã đã hết hạn');
    expect(chargeStatusLabel('VALID')).toBe('');
    expect(chargeStatusLabel('UNPRICED')).toBe('Chưa có giá');
    expect(formatDateTime('')).toBe('');
    expect(formatDateTime('invalid-date')).toBe('');
  });
});
