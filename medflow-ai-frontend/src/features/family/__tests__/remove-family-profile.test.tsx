import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { initialMockFamilyMembers } from '../data/family-mock';
import { FamilyProfileDetailPage } from '../pages/family-profile-detail-page';
import { FamilyProfileListPage } from '../pages/family-profile-list-page';

function renderDetailPage(memberId = 'FAM-002') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/family/${memberId}`]}>
        <Routes>
          <Route path="/family/:memberId" element={<FamilyProfileDetailPage />} />
          <Route path="/family" element={<div>Danh sách người thân page</div>} />
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

describe('Remove Family Profile (real API)', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'get').mockImplementation(async (url: string) => {
      if (url === '/patients/me/family-members') {
        return { data: initialMockFamilyMembers } as AxiosResponse;
      }
      const found = initialMockFamilyMembers.find((m) => url.endsWith(`/${m.id}`));
      if (!found) throw new Error('Not found');
      return { data: found } as AxiosResponse;
    });
    vi.spyOn(apiClient, 'delete').mockResolvedValue({ data: {} } as AxiosResponse);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('hiển thị nút "Gỡ liên kết hồ sơ" trên trang chi tiết và mở modal gỡ bỏ', async () => {
    renderDetailPage('FAM-002');

    const removeBtn = await screen.findByRole('button', { name: /Gỡ liên kết hồ sơ/i });
    fireEvent.click(removeBtn);

    expect(screen.getByText('Gỡ liên kết Hồ sơ Người thân')).toBeInTheDocument();
  });

  it('chặn gỡ liên kết khi người thân đang có lịch hẹn khám sắp tới (FAM-003)', async () => {
    renderDetailPage('FAM-003');

    const removeBtn = await screen.findByRole('button', { name: /Gỡ liên kết hồ sơ/i });
    fireEvent.click(removeBtn);

    expect(screen.getByText(/Không thể gỡ liên kết lúc này:/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Xác nhận gỡ liên kết/i })).not.toBeInTheDocument();
  });

  it('gọi DELETE /patients/me/family-members/:id khi xác nhận gỡ liên kết', async () => {
    const deleteSpy = vi.spyOn(apiClient, 'delete').mockResolvedValueOnce({} as AxiosResponse);
    renderDetailPage('FAM-002');

    fireEvent.click(await screen.findByRole('button', { name: /Gỡ liên kết hồ sơ/i }));
    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.change(screen.getByLabelText(/Lý do gỡ liên kết/i), {
      target: { value: 'Không còn nhu cầu quản lý thay' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận gỡ liên kết/i }));

    await waitFor(() => {
      expect(deleteSpy).toHaveBeenCalledWith('/patients/me/family-members/FAM-002', {
        data: { reason: 'Không còn nhu cầu quản lý thay' },
      });
      expect(screen.getByText('Danh sách người thân page')).toBeInTheDocument();
    });
  });

  it('không hiển thị nút gỡ liên kết cho tài khoản chính (FAM-001)', async () => {
    renderDetailPage('FAM-001');

    await screen.findAllByText('NGUYỄN VĂN AN');
    expect(screen.queryByRole('button', { name: /Gỡ liên kết hồ sơ/i })).not.toBeInTheDocument();
  });

  it('cho phép mở modal gỡ liên kết từ nút icon thùng rác trên trang danh sách', async () => {
    renderListPage();

    const removeCardBtn = await screen.findByRole('button', {
      name: /Gỡ liên kết NGUYỄN MINH KHANG/i,
    });
    fireEvent.click(removeCardBtn);

    expect(screen.getByText('Gỡ liên kết Hồ sơ Người thân')).toBeInTheDocument();
  });
});
