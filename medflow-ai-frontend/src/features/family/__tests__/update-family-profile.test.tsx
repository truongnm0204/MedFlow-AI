import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

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

describe('Update Family Profile (M1-033)', () => {
  it('mở modal cập nhật với đầy đủ thông tin điền sẵn và khóa các trường định danh', async () => {
    renderDetailPage();

    const editBtn = await screen.findByRole('button', { name: /Chỉnh sửa thông tin liên hệ/i });
    fireEvent.click(editBtn);

    expect(screen.getByText('Cập nhật Thông tin Hồ sơ Người thân')).toBeInTheDocument();
    expect(
      screen.getByText(/Để đảm bảo an toàn pháp lý của Hồ sơ bệnh án điện tử/),
    ).toBeInTheDocument();

    // Thông tin định danh pháp lý khóa
    expect(screen.getAllByText('NGUYỄN MINH KHANG').length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText('#PAT-2026-9042').length).toBeGreaterThanOrEqual(1);

    // Điền sẵn địa chỉ
    const addressInput = screen.getByLabelText(/Địa chỉ thường trú \/ nơi ở hiện tại/i);
    expect(addressInput).toHaveValue(
      'Số 45 Tràng Tiền, Phường Tràng Tiền, Quận Hoàn Kiếm, Thành phố Hà Nội',
    );
  });

  it('kiểm tra validation khi nhập sai số điện thoại và để trống địa chỉ', async () => {
    renderDetailPage();

    const editBtn = await screen.findByRole('button', { name: /Chỉnh sửa thông tin liên hệ/i });
    fireEvent.click(editBtn);

    const phoneInput = screen.getByLabelText(/Số điện thoại liên hệ chính/i);
    const addressInput = screen.getByLabelText(/Địa chỉ thường trú \/ nơi ở hiện tại/i);
    const submitBtn = screen.getByRole('button', { name: /Lưu thay đổi/i });

    // Nhập SĐT không hợp lệ
    fireEvent.change(phoneInput, { target: { value: '12345' } });
    fireEvent.change(addressInput, { target: { value: 'abc' } });
    fireEvent.click(submitBtn);

    expect(
      await screen.findByText(/Số điện thoại liên hệ không hợp lệ/i),
    ).toBeInTheDocument();
    expect(
      await screen.findByText(/Địa chỉ thường trú tối thiểu 5 ký tự/i),
    ).toBeInTheDocument();
  });

  it('cập nhật thành công thông tin người thân từ trang chi tiết', async () => {
    renderDetailPage();

    const updateBtn = await screen.findByRole('button', { name: /^Cập nhật$/i });
    fireEvent.click(updateBtn);

    const phoneInput = screen.getByLabelText(/Số điện thoại liên hệ chính/i);
    const addressInput = screen.getByLabelText(/Địa chỉ thường trú \/ nơi ở hiện tại/i);
    const submitBtn = screen.getByRole('button', { name: /Lưu thay đổi/i });

    fireEvent.change(phoneInput, { target: { value: '0987654321' } });
    fireEvent.change(addressInput, {
      target: { value: 'Số 100 Phố Huế, Quận Hai Bà Trưng, TP. Hà Nội' },
    });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.queryByText('Cập nhật Thông tin Hồ sơ Người thân')).not.toBeInTheDocument();
    });
  });

  it('mở modal sửa thông tin từ trang danh sách người thân', async () => {
    renderListPage();

    const editButtons = screen.getAllByRole('button', { name: /Sửa thông tin/i });
    expect(editButtons.length).toBeGreaterThan(0);
    fireEvent.click(editButtons[0]);

    expect(screen.getByText('Cập nhật Thông tin Hồ sơ Người thân')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Hủy bỏ/i })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Hủy bỏ/i }));
    await waitFor(() => {
      expect(screen.queryByText('Cập nhật Thông tin Hồ sơ Người thân')).not.toBeInTheDocument();
    });
  });
});
