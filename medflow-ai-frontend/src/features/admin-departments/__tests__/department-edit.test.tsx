import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { DepartmentEditPage } from '../pages/department-edit-page';

function renderPage(id = 'DEP-001') {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[`/admin/departments/${id}/edit`]}>
        <Routes>
          <Route path="/admin/departments/:id/edit" element={<DepartmentEditPage />} />
          <Route path="/admin/departments" element={<div>Danh mục Khoa phòng Mock</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DepartmentEditPage (SCR-ADM-DEP-02 real API)', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: {
        data: [
          {
            id: 1,
            code: 'DEP-001',
            name: 'Khoa Nội Tiêu hóa',
            hospitalId: 1,
            facility: 'MedFlow Cơ sở 1 - Hà Nội',
            isActive: true,
            status: 'ACTIVE',
          },
        ],
        total: 1,
        page: 1,
        pageSize: 100,
        totalPages: 1,
      },
    } as AxiosResponse);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('loads department from API, submits PUT /admin/departments/:id, and shows alert for unknown id', async () => {
    const putSpy = vi.spyOn(apiClient, 'put').mockResolvedValueOnce({
      data: {
        id: 1,
        code: 'DEP-001',
        name: 'Khoa Nội Mới',
        hospitalId: 1,
        facility: 'MedFlow Cơ sở 1 - Hà Nội',
        isActive: false,
        status: 'INACTIVE',
      },
    } as AxiosResponse);

    renderPage('DEP-001');

    const nameInput = await screen.findByLabelText('Tên khoa phòng *');
    expect((nameInput as HTMLInputElement).value).toBe('Khoa Nội Tiêu hóa');

    fireEvent.change(nameInput, { target: { value: 'Khoa Nội Mới' } });
    fireEvent.click(screen.getByLabelText('INACTIVE - Tạm ngừng'));
    fireEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }));

    await waitFor(() => {
      expect(putSpy).toHaveBeenCalledWith(
        '/admin/departments/1',
        expect.objectContaining({ name: 'Khoa Nội Mới', isActive: false }),
      );
      expect(screen.getByText('Danh mục Khoa phòng Mock')).toBeInTheDocument();
    });
  });

  it('shows not-found alert for unknown id', async () => {
    renderPage('DEP-999');
    expect(await screen.findByRole('alert')).toHaveTextContent('Không tìm thấy khoa phòng DEP-999');
  });
});
