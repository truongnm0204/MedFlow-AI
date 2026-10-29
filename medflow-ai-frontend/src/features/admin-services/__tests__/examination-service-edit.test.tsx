import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { INITIAL_SERVICES } from '../data/examination-services-data';
import { ServiceEditModal } from '../pages/service-edit-modal';
import { useExaminationServiceStore } from '../store/examination-service.store';

describe('ServiceEditModal (SCR-ADM-ESV-03)', () => {
  beforeEach(() => {
    useExaminationServiceStore.setState({
      services: [...INITIAL_SERVICES],
      priceHistoryService: null,
      draft: null,
    });
  });

  const renderModal = (serviceId = 'EX-1-01') =>
    render(
      <MemoryRouter initialEntries={[`/admin/services/${serviceId}/edit`]}>
        <Routes>
          <Route path="/admin/services/:serviceId/edit" element={<ServiceEditModal />} />
          <Route path="/admin/services" element={<div>Danh mục Dịch vụ Page Mock</div>} />
        </Routes>
      </MemoryRouter>,
    );

  it('renders modal dialog header, code badge, and operational notice', () => {
    renderModal('EX-1-01');

    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(
      screen.getByRole('heading', {
        name: /Dịch vụ Khám - Khám Nội Tổng quát & Tư vấn Sức khỏe/i,
      }),
    ).toBeInTheDocument();
    expect(screen.getByText('EX-1-01')).toBeInTheDocument();
    expect(screen.getByText('● Active')).toBeInTheDocument();
    expect(screen.getByText(/Lưu ý vận hành Khám Ngoại trú:/i)).toBeInTheDocument();
  });

  it('renders pre-populated values for the selected service', () => {
    renderModal('EX-1-01');

    const codeInput = screen.getByLabelText(/Mã Dịch vụ khám \(Service ID\)/i) as HTMLInputElement;
    expect(codeInput.value).toBe('EX-1-01');
    expect(codeInput).toHaveAttribute('readonly');

    const nameInput = screen.getByLabelText(/Tên Dịch vụ khám ngoại trú/i) as HTMLInputElement;
    expect(nameInput.value).toBe('Khám Nội Tổng quát & Tư vấn Sức khỏe');

    const deptSelect = screen.getByLabelText(
      /Khoa công tác trực thuộc \/ Chuyên môn quản lý/i,
    ) as HTMLSelectElement;
    expect(deptSelect.value).toBe('Khoa Khám bệnh');

    const categorySelect = screen.getByLabelText(/Phân loại dịch vụ/i) as HTMLSelectElement;
    expect(categorySelect.value).toBe('Khám Lâm sàng');

    const onlineCheckbox = screen.getByRole('checkbox', {
      name: /Cho phép đặt lịch trực tuyến/i,
    }) as HTMLInputElement;
    expect(onlineCheckbox.checked).toBe(true);
  });

  it('renders not found state when serviceId is invalid', () => {
    renderModal('EX-99-999');

    expect(screen.getByText('Không tìm thấy hồ sơ dịch vụ khám minh họa.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Quay lại danh sách/i })).toBeInTheDocument();
  });

  it('allows toggling online booking checkbox', () => {
    renderModal('EX-1-01');

    const onlineCheckbox = screen.getByRole('checkbox', {
      name: /Cho phép đặt lịch trực tuyến/i,
    }) as HTMLInputElement;
    expect(onlineCheckbox.checked).toBe(true);

    fireEvent.click(onlineCheckbox);
    expect(onlineCheckbox.checked).toBe(false);
  });

  it('allows toggling service status from active to suspended via footer button', () => {
    renderModal('EX-1-01');

    const suspendBtn = screen.getByRole('button', { name: /⊖ Tạm ngừng dịch vụ/i });
    fireEvent.click(suspendBtn);

    expect(screen.getByText(/Dịch vụ đã chuyển sang trạng thái Tạm dừng/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /⊕ Kích hoạt dịch vụ/i })).toBeInTheDocument();
  });

  it('submits updated values and navigates back to /admin/services', async () => {
    renderModal('EX-1-01');

    const nameInput = screen.getByLabelText(/Tên Dịch vụ khám ngoại trú/i);
    fireEvent.change(nameInput, {
      target: { value: 'Khám Nội Tổng quát & Tư vấn Chuyên sâu VIP' },
    });

    const submitBtn = screen.getByRole('button', { name: /Lưu dịch vụ khám/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.getByText('Danh mục Dịch vụ Page Mock')).toBeInTheDocument();
    });

    const updated = useExaminationServiceStore.getState().services.find((s) => s.id === 'EX-1-01');
    expect(updated?.name).toBe('Khám Nội Tổng quát & Tư vấn Chuyên sâu VIP');
  });

  it('navigates back when clicking close icon or cancel button', () => {
    renderModal('EX-1-01');

    const closeBtn = screen.getByRole('button', { name: 'Đóng chỉnh sửa' });
    fireEvent.click(closeBtn);
    expect(screen.getByText('Danh mục Dịch vụ Page Mock')).toBeInTheDocument();
  });

  it('navigates back when pressing Escape key', () => {
    renderModal('EX-1-01');

    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.getByText('Danh mục Dịch vụ Page Mock')).toBeInTheDocument();
  });

  it('triggers setPriceHistoryService when clicking Lịch sử button', () => {
    renderModal('EX-1-01');

    const historyBtn = screen.getByRole('button', { name: 'Lịch sử' });
    fireEvent.click(historyBtn);

    expect(useExaminationServiceStore.getState().priceHistoryService?.id).toBe('EX-1-01');
  });
});
