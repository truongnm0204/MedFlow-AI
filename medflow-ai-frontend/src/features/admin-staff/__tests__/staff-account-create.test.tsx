import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { StaffAccountCreatePage } from '../pages/staff-account-create-page';
import { staffCreateSchema } from '../schemas/staff-create.schema';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    post: vi.fn(),
  },
}));

const renderPage = () => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/admin/staff/new']}>
        <Routes>
          <Route path="/admin/staff/new" element={<StaffAccountCreatePage />} />
          <Route path="/admin/staff" element={<div>Staff List Mock</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
};

const valid = {
  staffCode: 'STF-0250',
  fullName: 'Lê Minh An',
  authProvider: 'LOCAL',
  facility: 'MedFlow Cơ sở 1 - Hà Nội',
  email: 'an.le@medflow.vn',
  password: 'password1',
  status: 'ACTIVE',
  role: 'DOCTOR',
  department: 'Khoa Tim mạch',
} as const;

describe('staffCreateSchema', () => {
  it('accepts valid input and validates password/email/code', () => {
    expect(staffCreateSchema.safeParse(valid).success).toBe(true);
    expect(staffCreateSchema.safeParse({ ...valid, password: 'short' }).success).toBe(false);
    expect(
      staffCreateSchema.safeParse({ ...valid, authProvider: 'GOOGLE', password: '' }).success,
    ).toBe(true);
    expect(staffCreateSchema.safeParse({ ...valid, email: 'nope' }).success).toBe(false);
    expect(staffCreateSchema.safeParse({ ...valid, staffCode: 'X1' }).success).toBe(false);
  });
});

describe('StaffAccountCreatePage (SCR-ADM-STAFF-02 real API)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiClient.post).mockResolvedValue({ data: { staffId: 250 } });
  });

  it('renders title, updates live preview, and validates empty submit', async () => {
    renderPage();
    expect(screen.getByRole('heading', { name: 'Tạo tài khoản nhân sự' })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/Họ tên nhân sự/), { target: { value: 'Lê Minh An' } });
    fireEvent.click(screen.getByRole('button', { name: 'Vai trò Điều dưỡng' }));
    const preview = screen.getByLabelText('Xem trước tài khoản');
    expect(preview).toHaveTextContent('Lê Minh An');
    expect(preview).toHaveTextContent('Điều dưỡng');

    fireEvent.click(screen.getByRole('button', { name: 'Tạo tài khoản' }));
    expect(await screen.findByText('Vui lòng nhập mã nhân sự')).toBeInTheDocument();
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it('calls POST /admin/staff-accounts and navigates to /admin/staff', async () => {
    const first = renderPage();
    fireEvent.change(screen.getByLabelText(/Mã nhân sự/), { target: { value: 'STF-0250' } });
    fireEvent.change(screen.getByLabelText(/Họ tên nhân sự/), { target: { value: 'Lê Minh An' } });
    fireEvent.change(screen.getByLabelText(/Email đăng nhập/), {
      target: { value: 'an.le@medflow.vn' },
    });
    fireEvent.change(screen.getByLabelText(/Mật khẩu tạm thời/), {
      target: { value: 'password1' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Tạo tài khoản' }));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/admin/staff-accounts',
        expect.objectContaining({
          staffCode: 'STF-0250',
          fullName: 'Lê Minh An',
          email: 'an.le@medflow.vn',
        }),
      );
      expect(screen.getByText('Staff List Mock')).toBeInTheDocument();
    });
    first.unmount();

    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Hủy bỏ' }));
    expect(screen.getByText('Staff List Mock')).toBeInTheDocument();
  });
});
