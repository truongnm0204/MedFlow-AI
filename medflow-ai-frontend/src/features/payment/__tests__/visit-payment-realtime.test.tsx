import { HubConnectionState } from '@microsoft/signalr';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';
import {
  resetRealtimeClientForTests,
  setRealtimeConnectionFactoryForTests,
  type HubConnectionLike,
} from '@/shared/lib/realtime-client';

import { PatientPaymentDetailPage } from '../pages/patient-payment-detail-page';
import { VisitPaymentPage } from '../pages/visit-payment-page';
import type { VisitChargesResponseDto } from '../types/payment.types';

vi.mock('@/shared/components/layout/app-header', () => ({
  AppHeader: () => <div data-testid="app-header" />,
}));
vi.mock('@/shared/components/layout/patient-footer', () => ({
  PatientFooter: () => <div data-testid="patient-footer" />,
}));

class TestPaymentHub implements HubConnectionLike {
  state: HubConnectionState = HubConnectionState.Disconnected;
  readonly handlers = new Map<string, Array<(payload: unknown) => void>>();
  readonly invoked: string[] = [];

  async start(): Promise<void> {
    this.state = HubConnectionState.Connected;
  }

  async stop(): Promise<void> {
    this.state = HubConnectionState.Disconnected;
  }

  async invoke<T = unknown>(methodName: string, ...args: unknown[]): Promise<T> {
    this.invoked.push(`${methodName}:${String(args[0] ?? '')}`);
    return {
      resourceKey: 'visit-payment:101',
      expiresAtUtc: new Date(Date.now() + 300_000).toISOString(),
    } as T;
  }

  on(methodName: string, newMethod: (...args: never[]) => void): void {
    const list = this.handlers.get(methodName) ?? [];
    list.push(newMethod as unknown as (payload: unknown) => void);
    this.handlers.set(methodName, list);
  }

  off(methodName: string): void {
    this.handlers.delete(methodName);
  }

  onreconnecting(): void {}
  onreconnected(): void {}
  onclose(): void {}

  emitPaymentChanged(visitId: number): void {
    for (const cb of this.handlers.get('VisitPaymentChanged') ?? []) {
      cb({ eventType: 'VisitPaymentChanged', visitId });
    }
  }
}

const pendingCharges: VisitChargesResponseDto = {
  visitId: 101,
  visitCode: 'VIS-202611-0101',
  visitStatus: 'CHECKED_IN',
  patientId: 1,
  charges: [],
  payableTotal: 150000,
  hasUnpricedPayableCharges: false,
  amountDue: 150000,
  currency: 'VND',
  recognizedPaidTotal: 0,
  settlementStatus: 'UNPAID',
  paymentId: 55,
  paymentCode: 'MF55',
  paymentStatus: 'PENDING',
  paymentAmount: 150000,
  paymentMethod: 'VIETQR',
  paidAt: null,
  externalReference: null,
  canPayByQr: false,
};

describe('VietQR Payment Realtime Sync (#831)', () => {
  beforeEach(async () => {
    localStorage.clear();
    await resetRealtimeClientForTests();
    useAuthStore.getState().setAuth({
      user: {
        id: 1,
        email: 'patient@medflow.vn',
        fullName: 'Nguyễn Văn A',
        roles: ['PATIENT'],
        patientCode: 'PAT-0001',
        staffCode: null,
      },
      accessToken: 'jwt-valid',
      refreshToken: 'refresh-valid',
    });
  });

  afterEach(async () => {
    await resetRealtimeClientForTests();
    useAuthStore.getState().clearAuth();
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('VisitPaymentPage never marks PAID from SignalR payload alone and transitions immediately once REST confirms PAID', async () => {
    const hub = new TestPaymentHub();
    setRealtimeConnectionFactoryForTests(() => hub);

    let serverCharges: VisitChargesResponseDto = { ...pendingCharges };
    const getSpy = vi.spyOn(apiClient, 'get').mockImplementation(async () => ({
      data: serverCharges,
    }));

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/payments/visits/VIS-202611-0101']}>
          <Routes>
            <Route path="/payments/visits/:visitIdOrCode" element={<VisitPaymentPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(await screen.findByRole('button', { name: 'Tiếp tục thanh toán' })).toBeInTheDocument();
    await waitFor(() => expect(hub.invoked).toContain('JoinVisitPaymentGroup:VIS-202611-0101'));

    // SignalR fires while REST still says PENDING -> UI MUST remain unpaid (Single Source of Truth!)
    act(() => {
      hub.emitPaymentChanged(101);
    });
    await new Promise((r) => setTimeout(r, 320));
    expect(screen.queryByText('Đã thanh toán')).not.toBeInTheDocument();

    // Now REST transitions to PAID and SignalR fires VisitPaymentChanged -> UI flips to Đã thanh toán
    serverCharges = {
      ...pendingCharges,
      settlementStatus: 'PAID',
      paymentStatus: 'PAID',
      recognizedPaidTotal: 150000,
      amountDue: 0,
      paidAt: '2026-11-25T10:20:00',
      externalReference: 'FT-SEPAY-101',
    };

    act(() => {
      hub.emitPaymentChanged(101);
    });

    expect(await screen.findByText('Đã thanh toán')).toBeInTheDocument();
    expect(screen.getByText('FT-SEPAY-101')).toBeInTheDocument();
    expect(getSpy.mock.calls.length).toBeGreaterThanOrEqual(2);
  });

  it('PatientPaymentDetailPage updates to paid state upon VisitPaymentChanged REST refetch', async () => {
    const hub = new TestPaymentHub();
    setRealtimeConnectionFactoryForTests(() => hub);

    let serverCharges: VisitChargesResponseDto = { ...pendingCharges };
    vi.spyOn(apiClient, 'get').mockImplementation(async () => ({
      data: serverCharges,
    }));

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/payments/VIS-202611-0101']}>
          <Routes>
            <Route path="/payments/:visitCode" element={<PatientPaymentDetailPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect((await screen.findAllByText('MF55')).length).toBeGreaterThan(0);
    await waitFor(() => expect(hub.invoked).toContain('JoinVisitPaymentGroup:VIS-202611-0101'));

    serverCharges = {
      ...pendingCharges,
      settlementStatus: 'PAID',
      paymentStatus: 'PAID',
      recognizedPaidTotal: 150000,
      amountDue: 0,
      paidAt: '2026-11-25T10:22:00',
    };

    act(() => {
      hub.emitPaymentChanged(101);
    });

    expect(await screen.findByText(/đã được thanh toán đủ/)).toBeInTheDocument();
  });
});
