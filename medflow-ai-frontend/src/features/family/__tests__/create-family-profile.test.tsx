import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { initialMockFamilyMembers } from '../data/family-mock';
import { FamilyProfileListPage } from '../pages/family-profile-list-page';
import type { FamilyMember } from '../types/family.types';

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <FamilyProfileListPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('Create Family Profile (real API)', () => {
  let currentList: FamilyMember[] = [];

  beforeEach(() => {
    currentList = [...initialMockFamilyMembers];
    vi.spyOn(apiClient, 'get').mockImplementation(async () => {
      return { data: currentList } as AxiosResponse;
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('mở modal thêm người thân không chứa địa chỉ hay SĐT giả điền sẵn', async () => {
    renderPage();

    const addBtn = await screen.findByRole('button', { name: /Thêm người thân mới/i });
    fireEvent.click(addBtn);

    expect(screen.getByText('Thêm Hồ sơ Người thân')).toBeInTheDocument();
    expect(screen.queryByDisplayValue('0912 345 678')).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue('Nguyễn Văn An')).not.toBeInTheDocument();
  });

  it('kiểm tra hợp lệ dữ liệu và hiển thị thông báo lỗi khi nhập thiếu', async () => {
    renderPage();

    fireEvent.click(await screen.findByRole('button', { name: /Thêm người thân mới/i }));
    const submitBtn = screen.getByRole('button', { name: 'Thêm người thân' });
    fireEvent.click(submitBtn);

    expect(await screen.findByRole('alert')).toHaveTextContent(/Họ và tên tối thiểu 2 ký tự/i);
  });

  it('gọi POST /patients/me/family-members và làm mới danh sách khi thành công', async () => {
    const createdMember: FamilyMember = {
      ...initialMockFamilyMembers[1],
      id: 'FAM-005',
      fullName: 'NGUYỄN VĂN EM',
      patientCode: 'PAT-2026-0005',
    };
    const postSpy = vi.spyOn(apiClient, 'post').mockImplementation(async () => {
      currentList = [...currentList, createdMember];
      return { data: createdMember } as AxiosResponse;
    });

    renderPage();

    fireEvent.click(await screen.findByRole('button', { name: /Thêm người thân mới/i }));
    fireEvent.change(screen.getByPlaceholderText('VD: NGUYỄN VĂN B'), {
      target: { value: 'Nguyễn Văn Em' },
    });
    fireEvent.change(screen.getByPlaceholderText('15/08/2018'), {
      target: { value: '20/10/2019' },
    });
    fireEvent.change(screen.getByPlaceholderText(/Số nhà, đường/i), {
      target: { value: 'Số 10 Trần Hưng Đạo, Hoàn Kiếm, Hà Nội' },
    });

    fireEvent.click(screen.getByRole('button', { name: 'Thêm người thân' }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/patients/me/family-members',
        expect.objectContaining({ fullName: 'Nguyễn Văn Em' }),
      );
      expect(screen.getByText('NGUYỄN VĂN EM')).toBeInTheDocument();
    });
  });
});
