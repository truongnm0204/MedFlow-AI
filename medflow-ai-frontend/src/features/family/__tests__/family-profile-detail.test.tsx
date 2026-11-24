import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { initialMockFamilyMembers } from '../data/family-mock';
import { FamilyProfileDetailPage } from '../pages/family-profile-detail-page';

function renderDetailAtRoute(initialEntry: string) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/family/:memberId" element={<FamilyProfileDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('FamilyProfileDetailPage (real API)', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'get').mockImplementation(async (url: string) => {
      const found = initialMockFamilyMembers.find((m) => url.endsWith(`/${m.id}`));
      if (!found) {
        throw new Error('Không tìm thấy hồ sơ người thân hoặc bạn không có quyền truy cập.');
      }
      return { data: found } as AxiosResponse;
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('hiển thị đầy đủ thông tin định danh, sinh hiệu và liên hệ của người thân (FAM-002)', async () => {
    renderDetailAtRoute('/family/FAM-002');

    expect((await screen.findAllByText('NGUYỄN MINH KHANG')).length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText('#PAT-2026-9042')).toBeInTheDocument();
    expect(screen.getByText('128 cm')).toBeInTheDocument();
    expect(screen.getByText('26 kg')).toBeInTheDocument();
  });

  it('ẩn các nút chỉnh sửa khi hồ sơ người thân không có quyền canManage (FAM-004)', async () => {
    renderDetailAtRoute('/family/FAM-004');

    expect((await screen.findAllByText('LÊ THỊ MAI')).length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText('Chỉ đọc')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Chỉnh sửa thông tin liên hệ/i }),
    ).not.toBeInTheDocument();
  });

  it('hiển thị đầy đủ thông tin hồ sơ người thân là Vợ (FAM-003)', async () => {
    renderDetailAtRoute('/family/FAM-003');

    expect((await screen.findAllByText('NGUYỄN THỊ BÌNH')).length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText('nguyenthibinh.90@gmail.com')).toBeInTheDocument();
  });

  it('hiển thị trạng thái lỗi khi không tìm thấy hồ sơ người thân', async () => {
    renderDetailAtRoute('/family/FAM-999');

    expect(await screen.findByText('Không tìm thấy hồ sơ người thân')).toBeInTheDocument();
  });
});
