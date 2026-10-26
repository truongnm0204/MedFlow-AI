import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { resetStoredFamilyMembers } from '../data/family-mock';
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

describe('Remove Family Profile (M2-001)', () => {
  beforeEach(() => {
    resetStoredFamilyMembers();
  });

  it('hiển thị nút "Gỡ liên kết hồ sơ" trên trang chi tiết và mở modal gỡ bỏ', async () => {
    renderDetailPage('FAM-002');

    const removeBtn = await screen.findByRole('button', { name: /Gỡ liên kết hồ sơ/i });
    expect(removeBtn).toBeInTheDocument();

    fireEvent.click(removeBtn);

    // Modal hiển thị
    expect(screen.getByText('Gỡ liên kết Hồ sơ Người thân')).toBeInTheDocument();
    expect(screen.getByText(/Hủy ủy quyền quản lý hồ sơ khám chữa bệnh ngoại trú/i)).toBeInTheDocument();
    expect(screen.getByText(/Lưu ý bảo mật EHR:/i)).toBeInTheDocument();
  });

  it('chặn gỡ liên kết khi người thân đang có lịch hẹn khám sắp tới (FAM-003)', async () => {
    renderDetailPage('FAM-003');

    const removeBtn = await screen.findByRole('button', { name: /Gỡ liên kết hồ sơ/i });
    fireEvent.click(removeBtn);

    // Modal chặn
    expect(screen.getByText('Gỡ liên kết Hồ sơ Người thân')).toBeInTheDocument();
    expect(screen.getByText(/Không thể gỡ liên kết lúc này:/i)).toBeInTheDocument();
    expect(screen.getByText(/25\/11\/2026/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Đã hiểu & Quay lại/i })).toBeInTheDocument();

    // Không có nút xác nhận gỡ
    expect(screen.queryByRole('button', { name: /Xác nhận gỡ liên kết/i })).not.toBeInTheDocument();
  });

  it('cho phép xác nhận gỡ liên kết khi thỏa điều kiện và chọn lý do (FAM-002)', async () => {
    renderDetailPage('FAM-002');

    const removeBtn = await screen.findByRole('button', { name: /Gỡ liên kết hồ sơ/i });
    fireEvent.click(removeBtn);

    const submitBtn = screen.getByRole('button', { name: /Xác nhận gỡ liên kết/i });
    expect(submitBtn).toBeDisabled();

    // Tích chọn checkbox xác nhận
    const checkbox = screen.getByRole('checkbox');
    fireEvent.click(checkbox);
    expect(checkbox).toBeChecked();
    expect(submitBtn).not.toBeDisabled();

    // Chọn lý do khác
    const select = screen.getByLabelText(/Lý do gỡ liên kết/i);
    fireEvent.change(select, { target: { value: 'Không còn nhu cầu quản lý thay' } });

    // Click xác nhận gỡ
    fireEvent.click(submitBtn);

    // Điều hướng về /family sau khi gỡ thành công
    await waitFor(() => {
      expect(screen.getByText('Danh sách người thân page')).toBeInTheDocument();
    });
  });

  it('không hiển thị nút gỡ liên kết cho tài khoản chính (FAM-001)', async () => {
    renderDetailPage('FAM-001');

    const names = await screen.findAllByText('NGUYỄN VĂN AN');
    expect(names.length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: /Gỡ liên kết hồ sơ/i })).not.toBeInTheDocument();
  });

  it('cho phép mở modal gỡ liên kết từ nút icon thùng rác trên trang danh sách', async () => {
    renderListPage();

    const removeCardBtn = screen.getByRole('button', { name: /Gỡ liên kết NGUYỄN MINH KHANG/i });
    expect(removeCardBtn).toBeInTheDocument();

    fireEvent.click(removeCardBtn);

    expect(screen.getByText('Gỡ liên kết Hồ sơ Người thân')).toBeInTheDocument();
    expect(screen.getAllByText(/NGUYỄN MINH KHANG/i).length).toBeGreaterThan(0);
  });
});
