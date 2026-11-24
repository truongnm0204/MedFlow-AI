import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { StaffAccountEditPage } from '../pages/staff-account-edit-page';
import { staffUpdateSchema } from '../schemas/staff-update.schema';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    put: vi.fn(),
  },
}));

const renderPage = (id = 'STF-0142') => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[`/admin/staff/${id}/edit`]}>
        <Routes>
          <Route path="/admin/staff/:id/edit" element={<StaffAccountEditPage />} />
          <Route path="/admin/staff" element={<div>Staff list mock</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
};

describe('StaffAccountEditPage (real API)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiClient.get).mockResolvedValue({
      data: {
        data: [
          {
            staffId: 142,
            code: 'STF-0142',
            fullName: 'Lê Hoàng Minh',
            email: 'minh.le@medflow.vn',
            roleCode: 'DOCTOR',
            role: 'Bác sĩ',
            department: 'Khoa Nội Tiêu hóa',
            facility: 'MedFlow Cơ sở 1 - Hà Nội',
            status: 'ACTIVE',
            authProvider: 'LOCAL',
          },
        ],
      },
    });
    vi.mocked(apiClient.put).mockResolvedValue({ data: { staffId: 142 } });
  });

  it('pre-fills form from GET /admin/staff and updates live preview', async () => {
    renderPage();
    expect(await screen.findByDisplayValue('Lê Hoàng Minh')).toBeInTheDocument();
    const code = screen.getByDisplayValue('STF-0142') as HTMLInputElement;
    expect(code.readOnly).toBe(true);

    fireEvent.change(screen.getByDisplayValue('Lê Hoàng Minh'), {
      target: { value: 'Trần Văn B' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Vai trò Điều dưỡng' }));
    const preview = within(screen.getByTestId('staff-preview'));
    expect(preview.getByText('Trần Văn B')).toBeInTheDocument();
    expect(preview.getByText('Điều dưỡng')).toBeInTheDocument();
  });

  it('validates email, calls PUT /admin/staff-accounts/:id, navigates to /admin/staff, and shows alert for unknown id', async () => {
    const first = renderPage();
    expect(await screen.findByDisplayValue('minh.le@medflow.vn')).toBeInTheDocument();

    fireEvent.change(screen.getByDisplayValue('minh.le@medflow.vn'), {
      target: { value: 'not-an-email' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }));
    expect(screen.getByText('Email không hợp lệ')).toBeInTheDocument();
    expect(apiClient.put).not.toHaveBeenCalled();

    fireEvent.change(screen.getByDisplayValue('not-an-email'), {
      target: { value: 'minh.le@medflow.vn' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }));
    await waitFor(() => {
      expect(apiClient.put).toHaveBeenCalledWith(
        '/admin/staff-accounts/STF-0142',
        expect.objectContaining({ fullName: 'Lê Hoàng Minh', email: 'minh.le@medflow.vn' }),
      );
      expect(screen.getByText('Staff list mock')).toBeInTheDocument();
    });
    first.unmount();

    renderPage('STF-9999');
    expect(await screen.findByRole('alert')).toHaveTextContent('STF-9999');
  });
});

describe('staffUpdateSchema', () => {
  it('rejects empty name', () => {
    const result = staffUpdateSchema.safeParse({
      fullName: ' ',
      authMethod: 'LOCAL',
      facility: 'x',
      email: 'a@b.vn',
      status: 'ACTIVE',
      role: 'DOCTOR',
      department: 'y',
    });
    expect(result.success).toBe(false);
  });
});
