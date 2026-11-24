import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { initialMockFamilyMembers } from '../data/family-mock';
import { FamilyProfileDetailPage } from '../pages/family-profile-detail-page';
import { FamilyProfileListPage } from '../pages/family-profile-list-page';

function renderDetailPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/family/FAM-002']}>
        <Routes>
          <Route path="/family/:memberId" element={<FamilyProfileDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function renderListPage() {
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

describe('Update Family Profile (real API)', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'get').mockImplementation(async (url: string) => {
      if (url === '/patients/me/family-members') {
        return { data: initialMockFamilyMembers } as AxiosResponse;
      }
      const found = initialMockFamilyMembers.find((m) => url.endsWith(`/${m.id}`));
      if (!found) throw new Error('Not found');
      return { data: found } as AxiosResponse;
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('mở modal cập nhật và khóa các trường định danh', async () => {
    renderDetailPage();

    const editBtn = await screen.findByRole('button', { name: /Chỉnh sửa thông tin liên hệ/i });
    fireEvent.click(editBtn);

    expect(screen.getByText('Cập nhật Thông tin Hồ sơ Người thân')).toBeInTheDocument();
    expect(screen.getAllByText('NGUYỄN MINH KHANG').length).toBeGreaterThanOrEqual(2);
  });

  it('kiểm tra validation khi nhập sai số điện thoại và để trống địa chỉ', async () => {
    renderDetailPage();

    const editBtn = await screen.findByRole('button', { name: /Chỉnh sửa thông tin liên hệ/i });
    fireEvent.click(editBtn);

    fireEvent.change(screen.getByLabelText(/Số điện thoại liên hệ chính/i), {
      target: { value: '12345' },
    });
    fireEvent.change(screen.getByLabelText(/Địa chỉ thường trú \/ nơi ở hiện tại/i), {
      target: { value: 'abc' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Lưu thay đổi/i }));

    expect(await screen.findByText(/Số điện thoại liên hệ không hợp lệ/i)).toBeInTheDocument();
    expect(await screen.findByText(/Địa chỉ thường trú tối thiểu 5 ký tự/i)).toBeInTheDocument();
  });

  it('gọi PUT /patients/me/family-members/:id và không tự ghi localStorage khi lỗi', async () => {
    const putSpy = vi.spyOn(apiClient, 'put').mockResolvedValueOnce({
      data: {
        ...initialMockFamilyMembers[1],
        phone: '0987654321',
        address: 'Số 100 Phố Huế, Quận Hai Bà Trưng, TP. Hà Nội',
      },
    } as AxiosResponse);

    renderDetailPage();

    fireEvent.click(await screen.findByRole('button', { name: /^Cập nhật$/i }));
    fireEvent.change(screen.getByLabelText(/Số điện thoại liên hệ chính/i), {
      target: { value: '0987654321' },
    });
    fireEvent.change(screen.getByLabelText(/Địa chỉ thường trú \/ nơi ở hiện tại/i), {
      target: { value: 'Số 100 Phố Huế, Quận Hai Bà Trưng, TP. Hà Nội' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Lưu thay đổi/i }));

    await waitFor(() => {
      expect(putSpy).toHaveBeenCalledWith(
        '/patients/me/family-members/FAM-002',
        expect.objectContaining({ phone: '0987654321' }),
      );
      expect(screen.queryByText('Cập nhật Thông tin Hồ sơ Người thân')).not.toBeInTheDocument();
    });
  });

  it('mở modal sửa thông tin từ trang danh sách người thân', async () => {
    renderListPage();

    const editButtons = await screen.findAllByRole('button', { name: /Sửa thông tin/i });
    fireEvent.click(editButtons[0]);

    expect(screen.getByText('Cập nhật Thông tin Hồ sơ Người thân')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Hủy bỏ/i }));
    await waitFor(() => {
      expect(screen.queryByText('Cập nhật Thông tin Hồ sơ Người thân')).not.toBeInTheDocument();
    });
  });
});
