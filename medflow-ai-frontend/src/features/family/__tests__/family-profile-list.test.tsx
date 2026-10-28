import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

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

describe('FamilyProfileListPage', () => {
  it('hiển thị breadcrumb và tiêu đề màn hình', () => {
    renderPage();

    expect(screen.getByText('Quản lý Hồ sơ Người thân trong Gia đình')).toBeInTheDocument();
    expect(screen.getByText('4')).toBeInTheDocument();
    expect(screen.getByText('hồ sơ thành viên')).toBeInTheDocument();
  });

  it('hiển thị đầy đủ thông tin thẻ chủ tài khoản chính Nguyễn Văn An', () => {
    renderPage();

    expect(screen.getByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByText('Chủ tài khoản chính')).toBeInTheDocument();
    expect(screen.getByText('O+')).toBeInTheDocument();
    expect(screen.getByText('172 cm')).toBeInTheDocument();
    expect(screen.getByText('68 kg')).toBeInTheDocument();
    expect(screen.getByText('Xem hồ sơ chi tiết')).toBeInTheDocument();
  });

  it('hiển thị các thẻ thành viên gia đình được liên kết', () => {
    renderPage();

    // Con trai
    expect(screen.getByText('NGUYỄN MINH KHANG')).toBeInTheDocument();
    expect(screen.getByText('Con trai')).toBeInTheDocument();
    expect(screen.getByText(/Khám Nhi tự nguyện: Cần có phụ huynh đi cùng/)).toBeInTheDocument();

    // Vợ
    expect(screen.getByText('NGUYỄN THỊ BÌNH')).toBeInTheDocument();
    expect(screen.getByText('Vợ')).toBeInTheDocument();
    expect(screen.getByText('Lịch tái khám')).toBeInTheDocument();
    expect(screen.getByText('Gói dịch vụ')).toBeInTheDocument();

    // Mẹ ruột
    expect(screen.getByText('LÊ THỊ MAI')).toBeInTheDocument();
    expect(screen.getByText('Mẹ ruột')).toBeInTheDocument();
    expect(screen.getByText(/Ủy quyền nhận kết quả xét nghiệm/)).toBeInTheDocument();
  });

  it('hiển thị thẻ thêm người thân và nút thêm người thân mới', () => {
    renderPage();

    expect(screen.getByText('Thêm hồ sơ người thân mới')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /Thêm người thân/i }).length).toBeGreaterThan(0);
  });
});
