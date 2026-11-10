import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { FefoBatchSelectionPage } from '../pages/fefo-batch-selection-page';

function renderFefoPage() {
  useAuthStore.setState({
    user: {
      id: 6,
      email: 'duocsi@medflow.test',
      fullName: 'DS. Trần Thu Hương',
      roles: ['PHARMACY_STAFF'],
      patientCode: null,
    },
    accessToken: 'tok',
    refreshToken: 'rtok',
    isAuthenticated: true,
  });

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <FefoBatchSelectionPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('FefoBatchSelectionPage (Issue #145)', () => {
  it('renders FEFO batch recommendations and disables expired/blocked batches', () => {
    renderFefoPage();

    expect(screen.getByText('Bismuth subsalicylate 300mg')).toBeInTheDocument();
    expect(screen.getByText('LO-2405')).toBeInTheDocument();
    expect(screen.getByText('LO-2301')).toBeInTheDocument();
    expect(screen.getByText(/Lô đã quá hạn sử dụng theo kiểm định GPP/i)).toBeInTheDocument();

    const expiredBtn = screen.getByRole('button', { name: /LO-2301/i });
    expect(expiredBtn).toBeDisabled();

    const blockedBtn = screen.getByRole('button', { name: /NX-2399/i });
    expect(blockedBtn).toBeDisabled();
  });

  it('allows selecting an available alternative batch and confirming', () => {
    renderFefoPage();

    const altBatch = screen.getByRole('button', { name: /LO-2412/i });
    fireEvent.click(altBatch);
    expect(screen.getByText(/Đã chọn lô LO-2412/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Xác nhận chọn lô FEFO/i }));
    expect(
      screen.getByText(/Đã xác nhận phân bổ lô thuốc theo nguyên tắc FEFO/i),
    ).toBeInTheDocument();
  });
});
