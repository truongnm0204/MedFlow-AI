import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { initialMockFamilyMembers } from '../data/family-mock';
import { FamilyProfileListPage } from '../pages/family-profile-list-page';

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

describe('FamilyProfileListPage (real API)', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: initialMockFamilyMembers,
    } as AxiosResponse);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('gọi GET /patients/me/family-members và hiển thị tiêu đề cùng số lượng hồ sơ', async () => {
    renderPage();

    expect(await screen.findByText('Quản lý Hồ sơ Người thân trong Gia đình')).toBeInTheDocument();
    expect(await screen.findByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByText('4')).toBeInTheDocument();
    expect(screen.getByText('hồ sơ thành viên')).toBeInTheDocument();
  });

  it('hiển thị đầy đủ thông tin thẻ chủ tài khoản chính và các thành viên liên kết', async () => {
    renderPage();

    expect(await screen.findByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByText('Chủ tài khoản chính')).toBeInTheDocument();
    expect(screen.getByText('NGUYỄN MINH KHANG')).toBeInTheDocument();
    expect(screen.getByText('NGUYỄN THỊ BÌNH')).toBeInTheDocument();
    expect(screen.getByText('LÊ THỊ MAI')).toBeInTheDocument();
  });

  it('hiển thị trạng thái lỗi khi API thất bại thay vì lấy dữ liệu giả trong localStorage', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('Máy chủ từ chối kết nối'));
    renderPage();

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Không thể tải danh sách hồ sơ người thân',
    );
  });
});
