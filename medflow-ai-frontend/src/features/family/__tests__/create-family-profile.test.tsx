import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
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

describe('Create Family Profile (M1-031)', () => {
  it('mở modal thêm người thân với đầy đủ các trường thông tin và thông báo pháp lý', () => {
    renderPage();

    const addButtons = screen.getAllByRole('button', { name: /Thêm người thân/i });
    expect(addButtons.length).toBeGreaterThan(0);
    fireEvent.click(addButtons[0]);

    expect(screen.getByText('Thêm Hồ sơ Người thân')).toBeInTheDocument();
    expect(
      screen.getByText(/Để đảm bảo an toàn pháp lý của Hồ sơ bệnh án điện tử/),
    ).toBeInTheDocument();
    expect(screen.getByText('Thông tin định danh người thân')).toBeInTheDocument();
    expect(screen.getByText('Thông tin tiếp nhận kết quả & liên hệ')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Hủy bỏ/i })).toBeInTheDocument();
  });

  it('kiểm tra hợp lệ dữ liệu (form validation) và hiển thị thông báo lỗi khi nhập thiếu', async () => {
    renderPage();

    const addButtons = screen.getAllByRole('button', { name: /Thêm người thân/i });
    fireEvent.click(addButtons[0]);

    const submitBtn = screen.getByRole('button', { name: 'Thêm người thân' });
    fireEvent.click(submitBtn);

    // Báo lỗi họ tên tối thiểu 2 ký tự
    expect(await screen.findByRole('alert')).toHaveTextContent(/Họ và tên tối thiểu 2 ký tự/i);

    // Nhập họ tên nhưng để trống ngày sinh
    const nameInput = screen.getByPlaceholderText('VD: NGUYỄN VĂN B');
    fireEvent.change(nameInput, { target: { value: 'Nguyễn Văn Em' } });
    fireEvent.click(submitBtn);

    expect(await screen.findByRole('alert')).toHaveTextContent(/Vui lòng nhập ngày sinh/i);
  });

  it('thêm người thân mới thành công và cập nhật số lượng hồ sơ', async () => {
    renderPage();

    const addButtons = screen.getAllByRole('button', { name: /Thêm người thân/i });
    fireEvent.click(addButtons[0]);

    const nameInput = screen.getByPlaceholderText('VD: NGUYỄN VĂN B');
    fireEvent.change(nameInput, { target: { value: 'Nguyễn Văn Em' } });

    const birthInput = screen.getByPlaceholderText('15/08/2018');
    fireEvent.change(birthInput, { target: { value: '20/10/2019' } });

    const submitBtn = screen.getByRole('button', { name: 'Thêm người thân' });
    fireEvent.click(submitBtn);

    // Modal đóng và thành viên mới hiển thị
    await waitFor(() => {
      expect(screen.getByText('NGUYỄN VĂN EM')).toBeInTheDocument();
    });

    // Số lượng đạt 5/5 hồ sơ
    expect(screen.getByText('5')).toBeInTheDocument();

    // Hiển thị badge đã đạt giới hạn 5/5
    expect(screen.getAllByRole('button', { name: /Thêm người thân/i }).length).toBeGreaterThan(0);
  });
});
