import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { SlotBatchCloseModal } from '../components/slot-batch-close-modal';
import { SlotCloseModal } from '../components/slot-close-modal';
import { SlotReopenModal } from '../components/slot-reopen-modal';
import { ExaminationSlotListPage } from '../pages/examination-slot-list-page';
import { useExaminationSlotStore } from '../store/examination-slot.store';
import type { ExaminationSlot } from '../types/examination-slot.types';

const MOCK_SLOT_WITH_BOOKINGS: ExaminationSlot = {
  id: 'slt-test-01',
  code: 'SLT-TEST-01',
  doctorId: 'doc-001',
  doctorName: 'BS. Nguyễn Thu Lan',
  serviceId: 'srv-001',
  serviceName: 'Khám Nội tiết',
  serviceCode: 'EX-1-01',
  serviceType: 'OUTPATIENT',
  specialty: 'Nội tiết',
  room: 'PK-102',
  capacityBooked: 4,
  capacityMax: 8,
  date: '2026-10-27',
  dateDisplay: '27/10',
  startTime: '08:00',
  endTime: '08:30',
  status: 'OPEN',
};

const MOCK_SLOT_EMPTY: ExaminationSlot = {
  id: 'slt-test-02',
  code: 'SLT-TEST-02',
  doctorId: 'doc-002',
  doctorName: 'BS. Trần Hoài Nam',
  serviceId: 'srv-002',
  serviceName: 'Khám Tim mạch',
  serviceCode: 'EX-2-03',
  serviceType: 'OUTPATIENT',
  specialty: 'Tim mạch',
  room: 'PK-108',
  capacityBooked: 0,
  capacityMax: 8,
  date: '2026-10-27',
  dateDisplay: '27/10',
  startTime: '08:30',
  endTime: '09:00',
  status: 'OPEN',
};

const MOCK_SLOT_CLOSED: ExaminationSlot = {
  id: 'slt-test-03',
  code: 'SLT-TEST-03',
  doctorId: 'doc-003',
  doctorName: 'BS. Vũ Thị Bích Lan',
  serviceId: 'srv-001',
  serviceName: 'Khám Nội tiết',
  serviceCode: 'EX-1-01',
  serviceType: 'OUTPATIENT',
  specialty: 'Nội tiết',
  room: 'PK-102',
  capacityBooked: 2,
  capacityMax: 8,
  date: '2026-10-27',
  dateDisplay: '27/10',
  startTime: '09:00',
  endTime: '09:30',
  status: 'CLOSED',
  closeReason: 'Bác sĩ bận đột xuất / công tác / hội chẩn chuyên môn',
};

describe('Close Examination Slot (M2-065 / UC53)', () => {
  beforeEach(() => {
    useExaminationSlotStore.setState({
      slots: [MOCK_SLOT_WITH_BOOKINGS, MOCK_SLOT_EMPTY, MOCK_SLOT_CLOSED],
      selectedIds: [],
      searchQuery: '',
      dateFilter: '',
      specialtyFilter: '',
      statusFilter: '',
      page: 1,
      pageSize: 20,
    });
  });

  describe('SlotCloseModal', () => {
    it('renders warning and disables submit when slot has dependent bookings', () => {
      render(<SlotCloseModal slot={MOCK_SLOT_WITH_BOOKINGS} open={true} onClose={vi.fn()} />);

      expect(screen.getByText('Xác Nhận Đóng Khung Giờ Khám')).toBeInTheDocument();
      expect(
        screen.getByText(/Cảnh báo: Khung giờ có 4 lượt bệnh nhân đã đặt hẹn!/i),
      ).toBeInTheDocument();
      expect(screen.getByText('Giữ nguyên lượt hẹn đã đặt')).toBeInTheDocument();
      expect(screen.getByText('Yêu cầu liên hệ dời lịch / chuyển kíp')).toBeInTheDocument();

      // Submit button must be disabled until checkbox is checked
      const submitBtn = screen.getByRole('button', { name: /Xác nhận đóng khung giờ/i });
      expect(submitBtn).toBeDisabled();

      // Check the checkbox
      const checkbox = screen.getByLabelText(/Tôi đã nắm rõ số lượng/i);
      fireEvent.click(checkbox);
      expect(submitBtn).not.toBeDisabled();
    });

    it('renders clean info banner without blocking checkbox when slot has 0 bookings', () => {
      render(<SlotCloseModal slot={MOCK_SLOT_EMPTY} open={true} onClose={vi.fn()} />);

      expect(screen.getByText('Khung giờ chưa có lượt đặt nào (0/8 chỗ)')).toBeInTheDocument();
      expect(screen.queryByText(/Cảnh báo: Khung giờ có.*lượt bệnh nhân/i)).not.toBeInTheDocument();

      // Submit button is immediately enabled
      const submitBtn = screen.getByRole('button', { name: /Xác nhận đóng khung giờ/i });
      expect(submitBtn).not.toBeDisabled();
    });

    it('validates custom reason requires at least 5 characters when reasonCode is OTHER', async () => {
      render(<SlotCloseModal slot={MOCK_SLOT_EMPTY} open={true} onClose={vi.fn()} />);

      const select = screen.getByLabelText(/Lý do đóng khung giờ/i);
      fireEvent.change(select, { target: { value: 'OTHER' } });

      const submitBtn = screen.getByRole('button', { name: /Xác nhận đóng khung giờ/i });
      fireEvent.click(submitBtn);

      await waitFor(() => {
        expect(
          screen.getByText(/Vui lòng nhập chi tiết lý do đóng \(tối thiểu 5 ký tự\)/i),
        ).toBeInTheDocument();
      });

      // Type 5+ chars and re-submit
      const textarea = screen.getByPlaceholderText(/chi tiết lý do đóng khung giờ/i);
      fireEvent.change(textarea, { target: { value: 'Phòng khám cần phun khử khuẩn đột xuất' } });

      fireEvent.click(submitBtn);

      await waitFor(() => {
        expect(
          screen.queryByText(/Vui lòng nhập chi tiết lý do đóng \(tối thiểu 5 ký tự\)/i),
        ).not.toBeInTheDocument();
      });
    });

    it('closes slot successfully and updates status in store to CLOSED', async () => {
      const onSuccess = vi.fn();
      const onClose = vi.fn();

      render(
        <SlotCloseModal
          slot={MOCK_SLOT_EMPTY}
          open={true}
          onClose={onClose}
          onSuccess={onSuccess}
        />,
      );

      const submitBtn = screen.getByRole('button', { name: /Xác nhận đóng khung giờ/i });
      fireEvent.click(submitBtn);

      await waitFor(() => {
        expect(onSuccess).toHaveBeenCalled();
        expect(onClose).toHaveBeenCalled();
      });

      const updated = useExaminationSlotStore
        .getState()
        .slots.find((s) => s.id === MOCK_SLOT_EMPTY.id);
      expect(updated?.status).toBe('CLOSED');
      expect(updated?.closedAt).toBeDefined();
    });
  });

  describe('SlotReopenModal', () => {
    it('renders slot info and previous closure reason', () => {
      render(<SlotReopenModal slot={MOCK_SLOT_CLOSED} open={true} onClose={vi.fn()} />);

      expect(screen.getByText('Mở Lại Khung Giờ Khám')).toBeInTheDocument();
      expect(screen.getByText('BS. Vũ Thị Bích Lan')).toBeInTheDocument();
      expect(screen.getByText(/Lý do đóng trước đó:/i)).toBeInTheDocument();
    });

    it('reopens slot successfully and restores status to OPEN', async () => {
      const onSuccess = vi.fn();
      const onClose = vi.fn();

      render(
        <SlotReopenModal
          slot={MOCK_SLOT_CLOSED}
          open={true}
          onClose={onClose}
          onSuccess={onSuccess}
        />,
      );

      const confirmBtn = screen.getByRole('button', { name: /Xác nhận mở lại khung giờ/i });
      fireEvent.click(confirmBtn);

      await waitFor(() => {
        expect(onSuccess).toHaveBeenCalled();
        expect(onClose).toHaveBeenCalled();
      });

      const updated = useExaminationSlotStore
        .getState()
        .slots.find((s) => s.id === MOCK_SLOT_CLOSED.id);
      expect(updated?.status).toBe('OPEN');
      expect(updated?.closedAt).toBeUndefined();
    });
  });

  describe('SlotBatchCloseModal', () => {
    it('renders summary of selected slots and warns about total bookings', () => {
      render(
        <SlotBatchCloseModal
          slots={[MOCK_SLOT_WITH_BOOKINGS, MOCK_SLOT_EMPTY]}
          open={true}
          onClose={vi.fn()}
        />,
      );

      expect(screen.getByText('Đóng Hàng Loạt Khung Giờ Khám')).toBeInTheDocument();
      expect(screen.getByText('Số lượng khung giờ đã chọn:')).toBeInTheDocument();
      expect(
        screen.getByText(/Cảnh báo: Có tổng cộng 4 lượt đặt trong 1 khung giờ!/i),
      ).toBeInTheDocument();

      const submitBtn = screen.getByRole('button', { name: /Xác nhận đóng 2 khung giờ/i });
      expect(submitBtn).toBeDisabled();

      const checkbox = screen.getByLabelText(/Tôi đã nắm rõ số lượng 4 lượt đặt trước/i);
      fireEvent.click(checkbox);
      expect(submitBtn).not.toBeDisabled();
    });
  });

  describe('ExaminationSlotListPage integration', () => {
    it('allows closing an open slot via table row action and shows success feedback', async () => {
      render(
        <MemoryRouter>
          <ExaminationSlotListPage />
        </MemoryRouter>,
      );

      // Open slot has "Đóng khung giờ SLT-TEST-02" button
      const closeBtn = screen.getByRole('button', {
        name: `Đóng khung giờ ${MOCK_SLOT_EMPTY.code}`,
      });
      expect(closeBtn).toBeInTheDocument();
      fireEvent.click(closeBtn);

      // Modal appears
      expect(screen.getByText('Xác Nhận Đóng Khung Giờ Khám')).toBeInTheDocument();

      // Submit close
      const submitBtn = screen.getByRole('button', { name: /Xác nhận đóng khung giờ/i });
      fireEvent.click(submitBtn);

      await waitFor(() => {
        expect(screen.getByText(/đã được đóng thành công/i)).toBeInTheDocument();
      });
    });

    it('allows reopening a closed slot via table row action', async () => {
      render(
        <MemoryRouter>
          <ExaminationSlotListPage />
        </MemoryRouter>,
      );

      // Closed slot has "Mở lại khung giờ SLT-TEST-03" button
      const reopenBtn = screen.getByRole('button', {
        name: `Mở lại khung giờ ${MOCK_SLOT_CLOSED.code}`,
      });
      expect(reopenBtn).toBeInTheDocument();
      fireEvent.click(reopenBtn);

      // Reopen modal appears
      expect(screen.getByText('Mở Lại Khung Giờ Khám')).toBeInTheDocument();

      const confirmBtn = screen.getByRole('button', { name: /Xác nhận mở lại khung giờ/i });
      fireEvent.click(confirmBtn);

      await waitFor(() => {
        expect(screen.getByText(/đã được mở lại thành công/i)).toBeInTheDocument();
      });
    });
  });
});
