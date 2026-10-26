import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { PatientProfilePage } from '../pages/patient-profile-page';

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <PatientProfilePage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('PatientProfilePage', () => {
  it('hiển thị đầy đủ thông tin tổng quan bệnh nhân ở sidebar', () => {
    renderPage();

    expect(screen.getAllByText('NGUYỄN VĂN AN').length).toBe(2);
    expect(screen.getByText('Đã xác thực danh tính (CCCD)')).toBeInTheDocument();
    expect(screen.getByText('O+')).toBeInTheDocument();
    expect(screen.getByText('172 cm')).toBeInTheDocument();
    expect(screen.getByText('68 kg')).toBeInTheDocument();
    expect(screen.getByText('Hồ sơ người thân')).toBeInTheDocument();
    expect(screen.getByText('3 người')).toBeInTheDocument();
    expect(screen.getByText('Lịch sử khám ngoại trú')).toBeInTheDocument();
    expect(screen.getByText('4 lượt khám')).toBeInTheDocument();
    expect(screen.getByText('Đơn thuốc điện tử gần nhất')).toBeInTheDocument();
    expect(screen.getByText('1 đơn còn hạn')).toBeInTheDocument();
    expect(screen.getByText('Cần đặt lịch hẹn mới?')).toBeInTheDocument();
  });

  it('hiển thị chính xác thẻ thông tin định danh y tế (Read-only)', () => {
    renderPage();

    expect(screen.getByText('Thông tin định danh y tế')).toBeInTheDocument();
    expect(screen.getByText('Chỉ đọc (Read-only)')).toBeInTheDocument();
    expect(screen.getByText('15/08/1988')).toBeInTheDocument();
    expect(screen.getByText('Tuổi: 37')).toBeInTheDocument();
    expect(screen.getByText('Nam')).toBeInTheDocument();
    expect(screen.getByText('Việt Nam')).toBeInTheDocument();
    expect(screen.getByText(/001088\*\*\*\*\*\*/)).toBeInTheDocument();
  });

  it('hiển thị thẻ thông tin liên hệ, cư trú & người liên hệ khẩn cấp', () => {
    renderPage();

    expect(screen.getByText('Thông tin liên hệ & cư trú')).toBeInTheDocument();
    expect(screen.getByText('0912 345 678')).toBeInTheDocument();
    expect(screen.getByText('Đã xác thực SMS')).toBeInTheDocument();
    expect(screen.getByText('nguyenvana.patient@gmail.com')).toBeInTheDocument();
    expect(screen.getByText('Đã xác thực Email')).toBeInTheDocument();
    expect(
      screen.getByText('Số 45 Tràng Tiền, Phường Tràng Tiền, Quận Hoàn Kiếm, Thành phố Hà Nội'),
    ).toBeInTheDocument();

    expect(screen.getByText('Người liên hệ khẩn cấp (Emergency Contact)')).toBeInTheDocument();
    expect(screen.getByText('Nguyễn Thị Bình')).toBeInTheDocument();
    expect(screen.getByText('Vợ')).toBeInTheDocument();
    expect(screen.getByText('0988 765 432')).toBeInTheDocument();
  });

  it('hiển thị các nút hành động phía dưới', () => {
    renderPage();

    expect(screen.getByText('Chỉnh sửa thông tin liên hệ')).toBeInTheDocument();
    expect(screen.getByText('Quản lý người thân')).toBeInTheDocument();
    expect(screen.getByText('Tải tóm tắt hồ sơ y tế (PDF)')).toBeInTheDocument();
  });
});
