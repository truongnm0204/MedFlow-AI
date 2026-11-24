import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { DepartmentCreatePage } from '../pages/department-create-page';
import { departmentCreateSchema } from '../schemas/department-create.schema';

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/admin/departments/new']}>
        <Routes>
          <Route path="/admin/departments/new" element={<DepartmentCreatePage />} />
          <Route path="/admin/departments" element={<div>Danh mục Khoa phòng Mock</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('departmentCreateSchema', () => {
  it('accepts valid input', () => {
    const result = departmentCreateSchema.safeParse({
      code: 'DEP-015',
      name: 'Khoa Nội Tổng hợp',
      facilityId: '1',
      status: 'ACTIVE',
    });
    expect(result.success).toBe(true);
  });
});

describe('DepartmentCreatePage (SCR-ADM-DEP-02 real API)', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: [{ id: 1, hospitalCode: 'HOS-01', name: 'Bệnh viện MedFlow Hà Nội' }],
    } as AxiosResponse);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('loads real facilities and submits POST /admin/departments', async () => {
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: {
        id: 15,
        code: 'DEP-015',
        name: 'Khoa Nội Tổng hợp',
        hospitalId: 1,
        facility: 'Bệnh viện MedFlow Hà Nội',
        isActive: true,
        status: 'ACTIVE',
      },
    } as AxiosResponse);

    renderPage();
    expect(
      await screen.findByRole('option', { name: 'Bệnh viện MedFlow Hà Nội' }),
    ).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Mã khoa phòng *'), { target: { value: 'DEP-015' } });
    fireEvent.change(screen.getByLabelText('Tên khoa phòng *'), {
      target: { value: 'Khoa Nội Tổng hợp' },
    });
    fireEvent.change(screen.getByLabelText('Cơ sở *'), { target: { value: '1' } });

    expect(screen.getByTestId('preview-facility')).toHaveTextContent('Bệnh viện MedFlow Hà Nội');

    fireEvent.click(screen.getByRole('button', { name: 'Khai báo khoa phòng' }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith('/admin/departments', {
        code: 'DEP-015',
        name: 'Khoa Nội Tổng hợp',
        hospitalId: 1,
        isActive: true,
      });
      expect(screen.getByText('Danh mục Khoa phòng Mock')).toBeInTheDocument();
    });
  });
});
