import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { PatientIdentityClaimPage } from '../pages/patient-identity-claim-page';
import type { IdentityClaim } from '../types/identity-claim.types';

vi.mock('@/shared/components/layout/app-header', () => ({
  AppHeader: () => <div data-testid="app-header" />,
}));
vi.mock('@/shared/components/layout/patient-footer', () => ({
  PatientFooter: () => <div data-testid="patient-footer" />,
}));

const pendingClaim: IdentityClaim = {
  claimId: 7,
  status: 'PENDING',
  createdAt: '2026-11-14T09:00:00Z',
  expiresAt: '2026-11-21T09:00:00Z',
  decidedAt: null,
  decisionNote: null,
};

function renderPage(claims: IdentityClaim[] = []) {
  vi.spyOn(apiClient, 'get').mockImplementation(async (url) => {
    if (url === '/hospitals') {
      return { data: [{ id: 1, name: 'Bệnh viện A', isActive: true }] } as never;
    }
    if (url === '/patients/me/identity-claims') {
      return { data: claims } as never;
    }
    if (typeof url === 'string' && url.startsWith('/patients/me/identity-claims/')) {
      return { data: claims[0] ?? pendingClaim } as never;
    }
    return { data: [] } as never;
  });

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <PatientIdentityClaimPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('PatientIdentityClaimPage', () => {
  it('sends OTP and submits a claim', async () => {
    const user = userEvent.setup();
    renderPage();
    const post = vi.spyOn(apiClient, 'post').mockImplementation(async (url) => {
      if (url === '/patients/me/identity-claims/otp') {
        return { data: { expiresAt: '2026-11-14T09:05:00Z' } } as never;
      }
      return { data: pendingClaim } as never;
    });

    await user.type(await screen.findByLabelText('Mã hồ sơ bệnh nhân'), 'BN-2026-0100');
    await user.selectOptions(screen.getByLabelText('Cơ sở xác minh'), '1');
    await user.click(screen.getByRole('button', { name: /Gửi OTP/i }));
    expect(post).toHaveBeenCalledWith('/patients/me/identity-claims/otp');

    await user.type(screen.getByLabelText('Mã OTP'), '123456');
    await user.click(screen.getByRole('button', { name: /Gửi yêu cầu liên kết/i }));

    expect(post).toHaveBeenCalledWith('/patients/me/identity-claims', {
      patientCode: 'BN-2026-0100',
      hospitalId: 1,
      otp: '123456',
    });
    expect(await screen.findByText('Yêu cầu #7')).toBeInTheDocument();
    expect(screen.getByText('Đang chờ xác minh')).toBeInTheDocument();
  });

  it('renders approved state without the form', async () => {
    renderPage([{ ...pendingClaim, status: 'APPROVED', decidedAt: '2026-11-15T09:00:00Z' }]);

    expect(await screen.findByText('Đã liên kết hồ sơ')).toBeInTheDocument();
    expect(screen.queryByLabelText('Mã hồ sơ bệnh nhân')).not.toBeInTheDocument();
  });

  it('explains reconciliation state', async () => {
    renderPage([{ ...pendingClaim, status: 'NEEDS_RECONCILIATION' }]);

    expect(await screen.findByText('Đang đối soát hồ sơ')).toBeInTheDocument();
    expect(screen.getByText(/bộ phận quản lý hồ sơ đối soát/i)).toBeInTheDocument();
  });
});
