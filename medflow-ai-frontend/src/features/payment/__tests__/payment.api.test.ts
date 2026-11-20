import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { fetchMyVisitsApi, fetchVisitChargesApi, generateQrPaymentApi } from '../api/payment.api';
import type { QrPaymentResponseDto, VisitChargesResponseDto } from '../types/payment.types';

const qrDto: QrPaymentResponseDto = {
  visitId: 10,
  visitCode: 'VIS-202611-0001',
  paymentId: 5,
  paymentCode: 'MF5',
  amount: 120000,
  currency: 'VND',
  status: 'PENDING',
  qrPayload: '000201010212',
  bankBin: '970422',
  accountNumber: '0123456789',
  accountName: 'BENH VIEN DEMO',
  transferContent: 'MF5',
  issueKind: 'CREATED',
};

const chargesDto: VisitChargesResponseDto = {
  visitId: 10,
  visitCode: 'VIS-202611-0001',
  visitStatus: 'CHECKED_IN',
  patientId: 1,
  charges: [],
  payableTotal: 120000,
  hasUnpricedPayableCharges: false,
  amountDue: 120000,
  currency: 'VND',
  recognizedPaidTotal: 0,
  settlementStatus: 'UNPAID',
  paymentId: null,
  paymentCode: null,
  paymentStatus: 'UNPAID',
  paymentAmount: null,
  paymentMethod: 'VIETQR',
  paidAt: null,
  externalReference: null,
  canPayByQr: true,
};

afterEach(() => {
  vi.restoreAllMocks();
});

describe('payment.api', () => {
  it('encodes visit code and posts to the QR endpoint', async () => {
    const post = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: qrDto });

    await expect(generateQrPaymentApi(' VIS-1/2 ')).resolves.toEqual(qrDto);

    expect(post).toHaveBeenCalledWith('/patients/visits/VIS-1%2F2/payments/qr', {});
  });

  it('gets visit charges by visit code', async () => {
    const get = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: chargesDto });

    await expect(fetchVisitChargesApi('VIS-202611-0001')).resolves.toEqual(chargesDto);

    expect(get).toHaveBeenCalledWith('/patients/visits/VIS-202611-0001/charges');
  });

  it('gets my visits with the page limit', async () => {
    const get = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: { items: [] } });

    await expect(fetchMyVisitsApi()).resolves.toEqual({ items: [] });

    expect(get).toHaveBeenCalledWith('/patients/me/visits', { params: { limit: 20 } });
  });
});
