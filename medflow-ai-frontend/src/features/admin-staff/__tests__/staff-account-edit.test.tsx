import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { staffUpdateSchema } from '../schemas/staff-update.schema';
import { StaffAccountEditPage } from '../pages/staff-account-edit-page';

const renderPage = (id = 'STF-0142') =>
  render(
    <MemoryRouter initialEntries={[`/admin/staff/${id}/edit`]}>
      <Routes>
        <Route path="/admin/staff/:id/edit" element={<StaffAccountEditPage />} />
        <Route path="/admin/doctors" element={<div>Doctors list mock</div>} />
      </Routes>
    </MemoryRouter>,
  );

describe('StaffAccountEditPage', () => {
  it('pre-fills the form from mock data', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: 'Cập nhật tài khoản nhân sự' })).toBeInTheDocument();
    const code = screen.getByDisplayValue('STF-0142') as HTMLInputElement;
    expect(code.readOnly).toBe(true);
    expect(screen.getByDisplayValue('Lê Hoàng Minh')).toBeInTheDocument();
    expect(screen.getByDisplayValue('minh.le@medflow.vn')).toBeInTheDocument();
  });

  it('updates the live preview while editing', () => {
    renderPage();
    fireEvent.change(screen.getByDisplayValue('Lê Hoàng Minh'), {
      target: { value: 'Trần Văn B' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Vai trò Điều dưỡng' }));
    const preview = within(screen.getByTestId('staff-preview'));
    expect(preview.getByText('Trần Văn B')).toBeInTheDocument();
    expect(preview.getByText('Điều dưỡng')).toBeInTheDocument();
  });

  it('shows validation errors and blocks save for invalid email', () => {
    renderPage();
    fireEvent.change(screen.getByDisplayValue('minh.le@medflow.vn'), {
      target: { value: 'not-an-email' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }));
    expect(screen.getByText('Email không hợp lệ')).toBeInTheDocument();
    expect(screen.queryByText('Doctors list mock')).not.toBeInTheDocument();
  });

  it('navigates back on cancel and after valid save', () => {
    const first = renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Hủy bỏ' }));
    expect(screen.getByText('Doctors list mock')).toBeInTheDocument();
    first.unmount();
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }));
    expect(screen.getByText('Doctors list mock')).toBeInTheDocument();
  });

  it('shows not-found for unknown staff id', () => {
    renderPage('STF-9999');
    expect(screen.getByRole('alert')).toHaveTextContent('STF-9999');
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
