import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

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

describe('FamilyProfileDetailPage', () => {
  it('hiển thị đầy đủ thông tin định danh, sinh hiệu và liên hệ của người thân (FAM-002)', async () => {
    renderDetailAtRoute('/family/FAM-002');

    expect((await screen.findAllByText('NGUYỄN MINH KHANG')).length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText('#PAT-2026-9042')).toBeInTheDocument();
    expect(screen.getByText('128 cm')).toBeInTheDocument();
    expect(screen.getByText('26 kg')).toBeInTheDocument();
    expect(
      screen.getByText(/Khám Nhi tự nguyện: Cần có phụ huynh đi cùng khi thăm khám/),
    ).toBeInTheDocument();
    expect(screen.getByText('02/09/2026 (Nhi khoa Hô hấp)')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Chỉnh sửa thông tin liên hệ/i }),
    ).toBeInTheDocument();
  });

  it('ẩn các nút chỉnh sửa khi hồ sơ người thân không có quyền canManage (FAM-004)', async () => {
    renderDetailAtRoute('/family/FAM-004');

    expect((await screen.findAllByText('LÊ THỊ MAI')).length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText('Chỉ đọc')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Chỉnh sửa thông tin liên hệ/i }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Cập nhật$/i })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Đặt khám ngay/i })).toBeInTheDocument();
  });

  it('hiển thị đầy đủ thông tin hồ sơ người thân là Vợ (FAM-003)', async () => {
    renderDetailAtRoute('/family/FAM-003');

    expect((await screen.findAllByText('NGUYỄN THỊ BÌNH')).length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText('nguyenthibinh.90@gmail.com')).toBeInTheDocument();
    expect(screen.getByText('0988 765 432')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Chỉnh sửa thông tin liên hệ/i }),
    ).toBeInTheDocument();
  });

  it('hiển thị trạng thái lỗi khi không tìm thấy hồ sơ người thân', async () => {
    renderDetailAtRoute('/family/FAM-999');

    expect(await screen.findByText('Không tìm thấy hồ sơ người thân')).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: /Quay lại danh sách người thân/i }),
    ).toBeInTheDocument();
  });
});
