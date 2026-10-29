import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { staffCreateSchema } from '../schemas/staff-create.schema';
import { StaffAccountCreatePage } from '../pages/staff-account-create-page';

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={['/admin/staff/new']}>
      <Routes>
        <Route path="/admin/staff/new" element={<StaffAccountCreatePage />} />
        <Route path="/admin/doctors" element={<div>List Mock</div>} />
      </Routes>
    </MemoryRouter>,
  );

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
  it('accepts valid input', () => {
    expect(staffCreateSchema.safeParse(valid).success).toBe(true);
  });

  it('rejects short LOCAL password but allows empty for GOOGLE', () => {
    expect(staffCreateSchema.safeParse({ ...valid, password: 'short' }).success).toBe(false);
    expect(
      staffCreateSchema.safeParse({ ...valid, authProvider: 'GOOGLE', password: '' }).success,
    ).toBe(true);
  });

  it('rejects invalid email and staff code', () => {
    expect(staffCreateSchema.safeParse({ ...valid, email: 'nope' }).success).toBe(false);
    expect(staffCreateSchema.safeParse({ ...valid, staffCode: 'X1' }).success).toBe(false);
  });
});

describe('StaffAccountCreatePage (SCR-ADM-STAFF-02)', () => {
  it('renders title and sections', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: 'Tạo tài khoản nhân sự' })).toBeInTheDocument();
    expect(screen.getByText(/Hồ sơ nhân sự & đăng nhập/i)).toBeInTheDocument();
    expect(screen.getByText(/Vai trò & trạng thái/i)).toBeInTheDocument();
  });

  it('updates live preview as user types and picks role', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText(/Họ tên nhân sự/), { target: { value: 'Lê Minh An' } });
    fireEvent.change(screen.getByLabelText(/Email đăng nhập/), {
      target: { value: 'an.le@medflow.vn' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Vai trò Điều dưỡng' }));

    const preview = screen.getByLabelText('Xem trước tài khoản');
    expect(preview).toHaveTextContent('Lê Minh An');
    expect(preview).toHaveTextContent('an.le@medflow.vn');
    expect(preview).toHaveTextContent('Điều dưỡng');
  });

  it('shows validation errors on empty submit', async () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Tạo tài khoản' }));
    expect(await screen.findByText('Vui lòng nhập mã nhân sự')).toBeInTheDocument();
    expect(screen.getByText('Mật khẩu tạm thời tối thiểu 8 ký tự')).toBeInTheDocument();
  });

  it('navigates to list on valid submit', async () => {
    renderPage();
    fireEvent.change(screen.getByLabelText(/Mã nhân sự/), { target: { value: 'STF-0250' } });
    fireEvent.change(screen.getByLabelText(/Họ tên nhân sự/), { target: { value: 'Lê Minh An' } });
    fireEvent.change(screen.getByLabelText(/Email đăng nhập/), {
      target: { value: 'an.le@medflow.vn' },
    });
    fireEvent.change(screen.getByLabelText(/Mật khẩu tạm thời/), {
      target: { value: 'password1' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Tạo tài khoản' }));
    await waitFor(() => expect(screen.getByText('List Mock')).toBeInTheDocument());
  });

  it('cancels back to list', () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Hủy bỏ' }));
    expect(screen.getByText('List Mock')).toBeInTheDocument();
  });
});
