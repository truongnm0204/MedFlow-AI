import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { WalkInVisitPage } from '../pages/walk-in-visit-page';

function renderPage() {
  useAuthStore.setState({
    user: {
      id: 2,
      email: 'letan@medflow.test',
      fullName: 'Nhân viên Lễ tân',
      roles: ['RECEPTION'],
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
        <WalkInVisitPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('WalkInVisitPage (Issue #75)', () => {
  it('renders patient demographics and verified CCCD badge', () => {
    renderPage();

    expect(screen.getByText('Trần Minh Đức')).toBeInTheDocument();
    expect(screen.getByText('BN-2024-0021')).toBeInTheDocument();
    expect(screen.getByText(/CCCD đã xác thực/i)).toBeInTheDocument();
    expect(screen.getByText(/KHÁM NGOẠI TRÚ TỰ NGUYỆN/i)).toBeInTheDocument();
  });

  it('renders clinical priority options and updates selection on click', () => {
    renderPage();

    const priorityBtn = screen.getByRole('button', {
      name: /Ưu tiên người cao tuổi \/ Thai sản \/ Trẻ nhỏ/i,
    });
    fireEvent.click(priorityBtn);

    expect(priorityBtn).toHaveClass('ring-2');
  });

  it('updates available services when changing department', () => {
    renderPage();

    const deptSelect = screen.getByLabelText(/Khoa khám tiếp nhận/i);
    fireEvent.change(deptSelect, { target: { value: 'dept-cardio' } });

    expect(screen.getByText(/Khám Chuyên khoa Tim mạch Tự Nguyện/i)).toBeInTheDocument();
  });

  it('toggles symptoms on click', () => {
    renderPage();

    const chestPainBtn = screen.getByRole('button', { name: /Đau tức ngực/i });
    fireEvent.click(chestPainBtn);

    expect(screen.getByRole('button', { name: /Đau tức ngực ✓/i })).toBeInTheDocument();
  });

  it('displays validation error if reason for visit is too short', async () => {
    renderPage();

    const reasonInput = screen.getByLabelText(/Lý do đến khám/i);
    fireEvent.change(reasonInput, { target: { value: 'Đau' } });

    const submitBtn = screen.getByRole('button', {
      name: /Tạo lượt khám & Chuyển sang phân buồng/i,
    });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.getByText(/Lý do đến khám phải có ít nhất 5 ký tự/i)).toBeInTheDocument();
    });
  });

  it('submits valid form and displays success modal with visit code', async () => {
    renderPage();

    const submitBtn = screen.getByRole('button', {
      name: /Tạo lượt khám & Chuyển sang phân buồng/i,
    });
    fireEvent.click(submitBtn);

    await waitFor(
      () => {
        expect(screen.getByRole('dialog')).toBeInTheDocument();
        expect(screen.getByText(/Đã Tạo Lượt Khám Vãng Lai/i)).toBeInTheDocument();
        expect(screen.getByText('A019')).toBeInTheDocument();
        expect(screen.getByText('VIS-2026-0415-0145')).toBeInTheDocument();
      },
      { timeout: 2000 },
    );
  });
});
