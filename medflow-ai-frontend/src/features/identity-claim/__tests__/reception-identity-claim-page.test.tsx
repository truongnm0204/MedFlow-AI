import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { ReceptionIdentityClaimPage } from '../pages/reception-identity-claim-page';
import type { StaffIdentityClaim } from '../types/identity-claim.types';

vi.mock('@/features/reception/components/reception-sidebar', () => ({
  ReceptionSidebar: () => <div data-testid="reception-sidebar" />,
}));

const claim: StaffIdentityClaim = {
  claimId: 15,
  requesterUserId: 200,
  requestedPatientCode: 'BN-2026-0100',
  targetPatientId: 100,
  currentPatientId: 101,
  hospitalId: 1,
  status: 'PENDING',
  createdAt: '2026-11-14T09:00:00Z',
  expiresAt: '2026-11-21T09:00:00Z',
};

function renderPage() {
  vi.spyOn(apiClient, 'get').mockResolvedValue({ data: [claim] } as never);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <ReceptionIdentityClaimPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('ReceptionIdentityClaimPage', () => {
  it('approves a pending claim', async () => {
    const user = userEvent.setup();
    renderPage();
    const post = vi
      .spyOn(apiClient, 'post')
      .mockResolvedValue({ data: { ...claim, status: 'APPROVED' } } as never);

    expect(await screen.findByText('Hồ sơ: BN-2026-0100')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Phê duyệt/i }));

    expect(post).toHaveBeenCalledWith('/reception/patient-identity-claims/15/approve');
  });

  it('rejects a claim with a reason', async () => {
    const user = userEvent.setup();
    renderPage();
    const post = vi
      .spyOn(apiClient, 'post')
      .mockResolvedValue({ data: { ...claim, status: 'REJECTED' } } as never);

    await user.click(await screen.findByRole('button', { name: /Từ chối/i }));
    await user.type(screen.getByLabelText('Lý do từ chối'), 'Giấy tờ không khớp hồ sơ');
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Từ chối' }));

    expect(post).toHaveBeenCalledWith('/reception/patient-identity-claims/15/reject', {
      reason: 'Giấy tờ không khớp hồ sơ',
    });
  });
});
