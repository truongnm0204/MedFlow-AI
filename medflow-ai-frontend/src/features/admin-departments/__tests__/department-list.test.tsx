import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { DepartmentListPage } from '../pages/department-list-page';

const SAMPLE_DEPARTMENTS = [
  {
    id: 1,
    code: 'DEP-001',
    name: 'Khoa Nội Tiêu hóa',
    hospitalId: 1,
    facility: 'MedFlow Cơ sở 1',
    isActive: true,
    status: 'ACTIVE',
  },
  {
    id: 3,
    code: 'DEP-003',
    name: 'Khoa Nội tiết',
    hospitalId: 1,
    facility: 'MedFlow Cơ sở 1',
    isActive: true,
    status: 'ACTIVE',
  },
  {
    id: 13,
    code: 'DEP-013',
    name: 'Khoa Da liễu',
    hospitalId: 1,
    facility: 'MedFlow Cơ sở 1',
    isActive: false,
    status: 'INACTIVE',
  },
];

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <DepartmentListPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DepartmentListPage (SCR-ADM-DEP-01 real API)', () => {
  let currentList = [...SAMPLE_DEPARTMENTS];

  beforeEach(() => {
    currentList = [...SAMPLE_DEPARTMENTS];
    vi.spyOn(apiClient, 'get').mockImplementation(async () => {
      return {
        data: {
          data: currentList,
          total: currentList.length,
          page: 1,
          pageSize: 100,
          totalPages: 1,
        },
      } as AxiosResponse;
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('fetches GET /admin/departments and renders metric cards and rows', async () => {
    renderPage();

    expect(await screen.findByText('Khoa Nội Tiêu hóa')).toBeInTheDocument();
    expect(screen.getByText('DEP-001')).toBeInTheDocument();
    expect(screen.getByText('2 khoa đang hoạt động')).toBeInTheDocument();
  });

  it('filters by search and status', async () => {
    renderPage();
    await screen.findByText('Khoa Nội Tiêu hóa');

    fireEvent.change(screen.getByLabelText('Tìm theo mã khoa hoặc tên khoa'), {
      target: { value: 'dep-003' },
    });
    expect(screen.getByText('Khoa Nội tiết')).toBeInTheDocument();
    expect(screen.queryByText('DEP-001')).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Tìm theo mã khoa hoặc tên khoa'), {
      target: { value: '' },
    });
    fireEvent.change(screen.getByLabelText('Lọc theo trạng thái'), {
      target: { value: 'INACTIVE' },
    });
    expect(screen.getByText('Khoa Da liễu')).toBeInTheDocument();
    expect(screen.queryByText('DEP-001')).not.toBeInTheDocument();
  });

  it('calls PUT /admin/departments/:id on deactivate and reactivate', async () => {
    const putSpy = vi.spyOn(apiClient, 'put').mockImplementation(async (url, body) => {
      const idNum = Number(url.split('/').pop());
      const payload = body as { isActive: boolean };
      currentList = currentList.map((d) =>
        d.id === idNum
          ? { ...d, isActive: payload.isActive, status: payload.isActive ? 'ACTIVE' : 'INACTIVE' }
          : d,
      );
      return { data: currentList.find((d) => d.id === idNum) } as AxiosResponse;
    });

    renderPage();
    await screen.findByText('Khoa Nội tiết');

    fireEvent.click(screen.getByRole('button', { name: 'Ngừng hoạt động Khoa Nội tiết' }));
    fireEvent.click(screen.getByRole('button', { name: 'Ngừng hoạt động' }));

    await waitFor(() => {
      expect(putSpy).toHaveBeenCalledWith(
        '/admin/departments/3',
        expect.objectContaining({ isActive: false }),
      );
      expect(screen.getByText(/Đã ngừng hoạt động khoa phòng Khoa Nội tiết/i)).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: 'Kích hoạt lại Khoa Da liễu' }));
    await waitFor(() => {
      expect(putSpy).toHaveBeenCalledWith(
        '/admin/departments/13',
        expect.objectContaining({ isActive: true }),
      );
    });
  });
});
