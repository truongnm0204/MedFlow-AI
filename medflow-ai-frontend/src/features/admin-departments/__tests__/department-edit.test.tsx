import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { DepartmentEditPage } from '../pages/department-edit-page';

function renderPage(id = 'DEP-001') {
  return render(
    <MemoryRouter initialEntries={[`/admin/departments/${id}/edit`]}>
      <Routes>
        <Route path="/admin/departments/:id/edit" element={<DepartmentEditPage />} />
        <Route path="/admin/departments" element={<div>Danh mục Khoa phòng Mock</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('DepartmentEditPage (SCR-ADM-DEP-02)', () => {
  it('pre-fills form from mock data with readonly code', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: 'Chỉnh sửa khoa phòng' })).toBeInTheDocument();
    const code = screen.getByLabelText('Mã khoa phòng *') as HTMLInputElement;
    expect(code.value).toBe('DEP-001');
    expect(code).toHaveAttribute('readonly');
    expect((screen.getByLabelText('Tên khoa phòng *') as HTMLInputElement).value).toBe(
      'Khoa Nội Tiêu hóa',
    );
  });

  it('updates live preview as user edits', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Tên khoa phòng *'), {
      target: { value: 'Khoa Mới' },
    });
    fireEvent.click(screen.getByLabelText('INACTIVE - Tạm ngừng'));
    expect(screen.getByTestId('preview-name')).toHaveTextContent('Khoa Mới');
    expect(screen.getByTestId('preview-status')).toHaveTextContent('Tạm ngừng');
  });

  it('shows validation error for empty name', async () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Tên khoa phòng *'), { target: { value: '  ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }));
    expect(await screen.findByText('Vui lòng nhập tên khoa phòng')).toBeInTheDocument();
  });

  it('navigates back to list on cancel and on save', async () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Hủy bỏ' }));
    expect(screen.getByText('Danh mục Khoa phòng Mock')).toBeInTheDocument();
  });

  it('saves and returns to list', async () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }));
    await waitFor(() => expect(screen.getByText('Danh mục Khoa phòng Mock')).toBeInTheDocument());
  });

  it('shows not-found message for unknown id', () => {
    renderPage('DEP-999');
    expect(screen.getByRole('alert')).toHaveTextContent('Không tìm thấy khoa phòng DEP-999');
  });
});
