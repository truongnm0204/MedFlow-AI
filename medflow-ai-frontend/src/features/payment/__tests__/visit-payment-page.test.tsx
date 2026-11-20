import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AxiosError } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { VisitPaymentPage } from '../pages/visit-payment-page';
import type { QrPaymentResponseDto, VisitChargesResponseDto } from '../types/payment.types';

vi.mock('@/shared/components/layout/app-header', () => ({
  AppHeader: () => <div data-testid="app-header" />,
}));
vi.mock('@/shared/components/layout/patient-footer', () => ({
  PatientFooter: () => <div data-testid="patient-footer" />,
}));

const visitCode = 'VIS-202611-0001';

const baseCharges: VisitChargesResponseDto = {
  visitId: 10,
  visitCode,
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

const qrResponse: QrPaymentResponseDto = {
  visitId: 10,
  visitCode,
  paymentId: 5,
  paymentCode: 'MF5',
  amount: 120000,
  currency: 'VND',
  status: 'PENDING',
  qrPayload: '0002010102123854',
  bankBin: '970422',
  accountNumber: '0123456789',
  accountName: 'BENH VIEN DEMO',
  transferContent: 'MF5',
  issueKind: 'CREATED',
};

function renderPage(charges: VisitChargesResponseDto) {
  const get = vi.spyOn(apiClient, 'get').mockResolvedValue({ data: charges });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/payments/visits/${visitCode}`]}>
        <Routes>
          <Route path="/payments/visits/:visitIdOrCode" element={<VisitPaymentPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );

  return { get };
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('VisitPaymentPage', () => {
  it('shows the amount due and renders the QR after creating it', async () => {
    const user = userEvent.setup();
    const { get } = renderPage(baseCharges);
    const post = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: qrResponse });

    expect(await screen.findByText('120.000 VNĐ')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Tạo mã QR thanh toán/i }));

    expect(post).toHaveBeenCalledWith(`/visits/${visitCode}/payments/qr`);
    expect(get).toHaveBeenCalledWith(`/visits/${visitCode}/charges`);
    expect(await screen.findByText('Quét mã để chuyển khoản')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Mã QR thanh toán MF5' })).toBeInTheDocument();
    expect(screen.getByText('0123456789')).toBeInTheDocument();
    expect(screen.getAllByText('MF5').length).toBeGreaterThan(0);
  });

  it('shows the paid state without a create action', async () => {
    renderPage({
      ...baseCharges,
      settlementStatus: 'PAID',
      paymentStatus: 'PAID',
      paymentAmount: 120000,
      paidAt: '2026-11-14T10:15:00',
      externalReference: 'FT-9001',
      canPayByQr: false,
    });

    expect(await screen.findByText('Đã thanh toán')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Tạo mã QR/i })).not.toBeInTheDocument();
    expect(screen.getByText('FT-9001')).toBeInTheDocument();
  });

  it('offers to resume when a QR is still pending', async () => {
    renderPage({
      ...baseCharges,
      canPayByQr: false,
      paymentStatus: 'PENDING',
      paymentCode: 'MF5',
      paymentId: 5,
    });

    expect(await screen.findByText('120.000 VNĐ')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Tiếp tục thanh toán' })).toBeEnabled();
  });

  it('offers a new QR when the previous one expired', async () => {
    renderPage({
      ...baseCharges,
      paymentStatus: 'EXPIRED',
      paymentCode: 'MF5',
      paymentId: 5,
    });

    expect(await screen.findByText('Mã QR đã hết hạn')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Tạo mã QR mới' })).toBeEnabled();
  });

  it('explains why QR cannot be created', async () => {
    renderPage({
      ...baseCharges,
      amountDue: 0,
      payableTotal: 0,
      canPayByQr: false,
    });

    expect(await screen.findByText('Chưa thể tạo mã QR')).toBeInTheDocument();
    expect(screen.getByText('Lượt khám không có khoản phải thu.')).toBeInTheDocument();
  });

  it('surfaces the backend conflict message', async () => {
    const user = userEvent.setup();
    renderPage(baseCharges);
    const conflict = new AxiosError('Request failed with status code 409', 'ERR_BAD_REQUEST');
    conflict.response = {
      status: 409,
      statusText: 'Conflict',
      headers: {},
      config: { headers: {} } as never,
      data: { title: 'Xung đột dữ liệu', detail: 'Lượt khám được thu tại quầy.' },
    };
    vi.spyOn(apiClient, 'post').mockRejectedValueOnce(conflict);

    await user.click(await screen.findByRole('button', { name: /Tạo mã QR thanh toán/i }));

    expect(await screen.findByText('Lượt khám được thu tại quầy.')).toBeInTheDocument();
  });
});
