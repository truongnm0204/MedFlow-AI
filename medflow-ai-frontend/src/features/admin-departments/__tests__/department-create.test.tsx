import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { DepartmentCreatePage } from '../pages/department-create-page';
import { departmentCreateSchema } from '../schemas/department-create.schema';

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={['/admin/departments/new']}>
      <Routes>
        <Route path="/admin/departments/new" element={<DepartmentCreatePage />} />
        <Route path="/admin/departments" element={<div>Danh mục Khoa phòng Mock</div>} />
      </Routes>
    </MemoryRouter>,
  );

describe('departmentCreateSchema', () => {
  it('accepts valid input', () => {
    const result = departmentCreateSchema.safeParse({
      code: 'DEP-015',
      name: 'Khoa Hồi sức tích cực',
      facilityId: 'FAC-001',
      status: 'ACTIVE',
    });
    expect(result.success).toBe(true);
  });

  it('rejects bad code and empty facility', () => {
    const result = departmentCreateSchema.safeParse({
      code: 'X1',
      name: 'Khoa',
      facilityId: '',
      status: 'ACTIVE',
    });
    expect(result.success).toBe(false);
  });
});

describe('DepartmentCreatePage (SCR-ADM-DEP-02)', () => {
  it('renders title and form fields', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: 'Khai báo khoa phòng' })).toBeInTheDocument();
    expect(screen.getByLabelText('Mã khoa phòng *')).toBeInTheDocument();
    expect(screen.getByLabelText('Tên khoa phòng *')).toBeInTheDocument();
    expect(screen.getByLabelText('Cơ sở *')).toBeInTheDocument();
    expect(screen.getByLabelText('Trạng thái *')).toBeInTheDocument();
  });

  it('updates live preview as the user types', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Mã khoa phòng *'), { target: { value: 'DEP-015' } });
    fireEvent.change(screen.getByLabelText('Tên khoa phòng *'), {
      target: { value: 'Khoa Hồi sức tích cực' },
    });
    fireEvent.change(screen.getByLabelText('Cơ sở *'), { target: { value: 'FAC-002' } });
    fireEvent.change(screen.getByLabelText('Trạng thái *'), { target: { value: 'INACTIVE' } });

    expect(screen.getByTestId('preview-code')).toHaveTextContent('DEP-015');
    expect(screen.getByTestId('preview-name')).toHaveTextContent('Khoa Hồi sức tích cực');
    expect(screen.getByTestId('preview-facility')).toHaveTextContent('MedFlow Cơ sở 2 - TP.HCM');
    expect(screen.getByTestId('preview-status')).toHaveTextContent('Tạm ngừng');
  });

  it('shows validation errors on empty submit', async () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Khai báo khoa phòng' }));
    expect(await screen.findByText('Vui lòng nhập mã khoa phòng')).toBeInTheDocument();
    expect(screen.getByText('Vui lòng chọn cơ sở')).toBeInTheDocument();
  });

  it('submits valid data and navigates to the list', async () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Mã khoa phòng *'), { target: { value: 'DEP-015' } });
    fireEvent.change(screen.getByLabelText('Tên khoa phòng *'), {
      target: { value: 'Khoa Hồi sức tích cực' },
    });
    fireEvent.change(screen.getByLabelText('Cơ sở *'), { target: { value: 'FAC-001' } });
    fireEvent.click(screen.getByRole('button', { name: 'Khai báo khoa phòng' }));
    await waitFor(() => expect(screen.getByText('Danh mục Khoa phòng Mock')).toBeInTheDocument());
  });

  it('cancel navigates back to the list', () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Hủy bỏ' }));
    expect(screen.getByText('Danh mục Khoa phòng Mock')).toBeInTheDocument();
  });
});
