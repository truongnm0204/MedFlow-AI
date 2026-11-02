import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { INITIAL_EXAMINATION_SLOTS } from '../data/examination-slots-mock';
import { SlotEditModal } from '../pages/slot-edit-modal';
import { useExaminationSlotStore } from '../store/examination-slot.store';

describe('SlotEditModal (SCR-ADM-SLT-03)', () => {
  beforeEach(() => {
    useExaminationSlotStore.setState({
      slots: [...INITIAL_EXAMINATION_SLOTS],
      selectedIds: [],
    });
  });

  const renderModal = (slotId = 'slt-001') =>
    render(
      <MemoryRouter initialEntries={[`/admin/slots/${slotId}/edit`]}>
        <Routes>
          <Route path="/admin/slots" element={<div>Danh sách Khung giờ Slot Mock</div>} />
          <Route path="/admin/slots/:slotId/edit" element={<SlotEditModal />} />
        </Routes>
      </MemoryRouter>,
    );

  it('renders modal dialog header, code badge, and operational notice', () => {
    renderModal('slt-001');

    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(
      screen.getByRole('heading', {
        name: /Khung giờ Khám - SLT-001/i,
      }),
    ).toBeInTheDocument();
    expect(screen.getByText('EX-1-01')).toBeInTheDocument();
    expect(screen.getByText('● Đang mở')).toBeInTheDocument();
    expect(screen.getByText(/Lưu ý vận hành Khám Ngoại trú:/i)).toBeInTheDocument();
  });

  it('renders pre-populated values for the selected slot', () => {
    renderModal('slt-001');

    const codeInput = screen.getByLabelText(/Mã Slot khám \(Slot ID\)/i) as HTMLInputElement;
    expect(codeInput.value).toBe('SLT-001');
    expect(codeInput).toHaveAttribute('readonly');

    const serviceTypeSelect = screen.getByLabelText(/Phân loại dịch vụ/i) as HTMLSelectElement;
    expect(serviceTypeSelect.value).toBe('OUTPATIENT');

    const serviceSelect = screen.getByLabelText(/^Dịch vụ khám/i) as HTMLSelectElement;
    expect(serviceSelect.value).toBe('srv-001');

    const specialtySelect = screen.getByLabelText(/Chuyên khoa/i) as HTMLSelectElement;
    expect(specialtySelect.value).toBe('Nội tiết');

    const doctorSelect = screen.getByLabelText(/Bác sĩ phụ trách/i) as HTMLSelectElement;
    expect(doctorSelect.value).toBe('doc-001');

    const roomSelect = screen.getByLabelText(/Phòng khám \/ Buồng khám/i) as HTMLSelectElement;
    expect(roomSelect.value).toBe('PK-102');

    const dateInput = screen.getByLabelText(/Ngày áp dụng/i) as HTMLInputElement;
    expect(dateInput.value).toBe('2026-10-27');

    const startTimeInput = screen.getByLabelText(/Giờ bắt đầu/i) as HTMLInputElement;
    expect(startTimeInput.value).toBe('08:00');

    const endTimeInput = screen.getByLabelText(/Giờ kết thúc/i) as HTMLInputElement;
    expect(endTimeInput.value).toBe('08:30');

    const capacityInput = screen.getByLabelText(/Sức chứa tiếp nhận tối đa/i) as HTMLInputElement;
    expect(capacityInput.value).toBe('8');

    const statusSelect = screen.getByLabelText(/Trạng thái khung giờ/i) as HTMLSelectElement;
    expect(statusSelect.value).toBe('OPEN');
  });

  it('renders not found state when slotId is invalid', () => {
    renderModal('SLT-INVALID-999');

    expect(
      screen.getByText('Không tìm thấy thông tin khung giờ khám minh họa.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Quay lại danh sách/i })).toBeInTheDocument();
  });

  it('allows toggling slot status from open to closed via footer button', () => {
    renderModal('slt-001');

    const closeBtn = screen.getByRole('button', { name: /⊖ Đóng khung giờ/i });
    fireEvent.click(closeBtn);

    expect(screen.getByText(/Khung giờ đã chuyển sang trạng thái Đã đóng/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /⊕ Mở lại khung giờ/i })).toBeInTheDocument();
  });

  it('allows selecting quick capacity presets', () => {
    renderModal('slt-001');

    const preset10Btn = screen.getByRole('button', { name: '10' });
    fireEvent.click(preset10Btn);

    const capacityInput = screen.getByLabelText(/Sức chứa tiếp nhận tối đa/i) as HTMLInputElement;
    expect(capacityInput.value).toBe('10');
  });

  it('submits updated values and navigates back to /admin/slots', async () => {
    renderModal('slt-001');

    const roomSelect = screen.getByLabelText(/Phòng khám \/ Buồng khám/i);
    fireEvent.change(roomSelect, { target: { value: 'PK-201' } });

    const capacityInput = screen.getByLabelText(/Sức chứa tiếp nhận tối đa/i);
    fireEvent.change(capacityInput, { target: { value: '12' } });

    const submitBtn = screen.getByRole('button', { name: /Lưu khung giờ khám/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.getByText('Danh sách Khung giờ Slot Mock')).toBeInTheDocument();
    });

    const updated = useExaminationSlotStore.getState().slots.find((s) => s.id === 'slt-001');
    expect(updated?.room).toBe('PK-201');
    expect(updated?.capacityMax).toBe(12);
  });

  it('navigates back when clicking close icon or cancel button', () => {
    renderModal('slt-001');

    const closeBtn = screen.getByRole('button', { name: 'Đóng chỉnh sửa' });
    fireEvent.click(closeBtn);
    expect(screen.getByText('Danh sách Khung giờ Slot Mock')).toBeInTheDocument();
  });

  it('navigates back when pressing Escape key', () => {
    renderModal('slt-001');

    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.getByText('Danh sách Khung giờ Slot Mock')).toBeInTheDocument();
  });

  it('displays validation error when end time is before or equal to start time', async () => {
    renderModal('slt-001');

    const endTimeInput = screen.getByLabelText(/Giờ kết thúc/i);
    fireEvent.change(endTimeInput, { target: { value: '07:30' } });

    const submitBtn = screen.getByRole('button', { name: /Lưu khung giờ khám/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.getByText('Giờ kết thúc phải sau giờ bắt đầu')).toBeInTheDocument();
    });
  });
});
