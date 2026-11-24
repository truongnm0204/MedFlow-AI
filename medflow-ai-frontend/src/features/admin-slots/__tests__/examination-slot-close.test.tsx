import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { SlotBatchCloseModal } from '../components/slot-batch-close-modal';
import { SlotCloseModal } from '../components/slot-close-modal';
import { SlotReopenModal } from '../components/slot-reopen-modal';
import { ExaminationSlotListPage } from '../pages/examination-slot-list-page';
import { useExaminationSlotStore } from '../store/examination-slot.store';
import type { ExaminationSlot } from '../types/examination-slot.types';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
  },
}));

const MOCK_SLOT_WITH_BOOKINGS: ExaminationSlot = {
  id: '101',
  code: 'SLT-TEST-01',
  doctorId: '1',
  doctorName: 'BS. Nguyễn Thu Lan',
  serviceId: '5',
  serviceName: 'Khám Nội tiết',
  serviceCode: 'EX-1-01',
  serviceType: 'OUTPATIENT',
  specialty: 'Nội tiết',
  room: 'PK-102',
  capacityBooked: 4,
  capacityMax: 8,
  date: '2026-11-27',
  dateDisplay: '27/11',
  startTime: '08:00',
  endTime: '08:30',
  status: 'OPEN',
};

const MOCK_SLOT_EMPTY: ExaminationSlot = {
  id: '102',
  code: 'SLT-TEST-02',
  doctorId: '2',
  doctorName: 'BS. Trần Hoài Nam',
  serviceId: '6',
  serviceName: 'Khám Tim mạch',
  serviceCode: 'EX-2-03',
  serviceType: 'OUTPATIENT',
  specialty: 'Tim mạch',
  room: 'PK-108',
  capacityBooked: 0,
  capacityMax: 8,
  date: '2026-11-27',
  dateDisplay: '27/11',
  startTime: '08:30',
  endTime: '09:00',
  status: 'OPEN',
};

const MOCK_SLOT_CLOSED: ExaminationSlot = {
  id: '103',
  code: 'SLT-TEST-03',
  doctorId: '3',
  doctorName: 'BS. Vũ Thị Bích Lan',
  serviceId: '5',
  serviceName: 'Khám Nội tiết',
  serviceCode: 'EX-1-01',
  serviceType: 'OUTPATIENT',
  specialty: 'Nội tiết',
  room: 'PK-102',
  capacityBooked: 2,
  capacityMax: 8,
  date: '2026-11-27',
  dateDisplay: '27/11',
  startTime: '09:00',
  endTime: '09:30',
  status: 'CLOSED',
  closeReason: 'Bác sĩ bận đột xuất / công tác / hội chẩn chuyên môn',
};

describe('Close Examination Slot (M2-065 / UC53 real API, no setTimeout mock)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
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

    vi.mocked(apiClient.get).mockResolvedValue({
      data: {
        data: [MOCK_SLOT_WITH_BOOKINGS, MOCK_SLOT_EMPTY, MOCK_SLOT_CLOSED],
      },
    });
    vi.mocked(apiClient.post).mockResolvedValue({
      data: { closedCount: 2 },
    });
  });

  it('guards dependent bookings, validates OTHER reason, and calls POST /admin/examination-slots/:id/close', async () => {
    const first = render(
      <SlotCloseModal slot={MOCK_SLOT_WITH_BOOKINGS} open={true} onClose={vi.fn()} />,
    );
    expect(
      screen.getByText(/Cảnh báo: Khung giờ có 4 lượt bệnh nhân đã đặt hẹn!/i),
    ).toBeInTheDocument();
    const guardedSubmit = screen.getByRole('button', { name: /Xác nhận đóng khung giờ/i });
    expect(guardedSubmit).toBeDisabled();
    fireEvent.click(screen.getByLabelText(/Tôi đã nắm rõ số lượng/i));
    expect(guardedSubmit).not.toBeDisabled();
    first.unmount();

    const onSuccess = vi.fn();
    const onClose = vi.fn();
    render(
      <SlotCloseModal slot={MOCK_SLOT_EMPTY} open={true} onClose={onClose} onSuccess={onSuccess} />,
    );

    fireEvent.change(screen.getByLabelText(/Lý do đóng khung giờ/i), {
      target: { value: 'OTHER' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận đóng khung giờ/i }));
    expect(
      await screen.findByText(/Vui lòng nhập chi tiết lý do đóng \(tối thiểu 5 ký tự\)/i),
    ).toBeInTheDocument();

    fireEvent.change(screen.getByPlaceholderText(/chi tiết lý do đóng khung giờ/i), {
      target: { value: 'Phòng khám cần phun khử khuẩn đột xuất' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận đóng khung giờ/i }));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/admin/examination-slots/102/close',
        expect.objectContaining({
          reasonCode: 'OTHER',
          note: 'Phòng khám cần phun khử khuẩn đột xuất',
        }),
      );
      expect(onSuccess).toHaveBeenCalled();
      expect(onClose).toHaveBeenCalled();
    });
  });

  it('calls POST /admin/examination-slots/:id/reopen and POST /admin/examination-slots/batch-close', async () => {
    const onSuccess = vi.fn();
    const first = render(
      <SlotReopenModal
        slot={MOCK_SLOT_CLOSED}
        open={true}
        onClose={vi.fn()}
        onSuccess={onSuccess}
      />,
    );
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận mở lại khung giờ/i }));
    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith('/admin/examination-slots/103/reopen', {});
      expect(onSuccess).toHaveBeenCalled();
    });
    first.unmount();

    const onBatchSuccess = vi.fn();
    render(
      <SlotBatchCloseModal
        slots={[MOCK_SLOT_WITH_BOOKINGS, MOCK_SLOT_EMPTY]}
        open={true}
        onClose={vi.fn()}
        onSuccess={onBatchSuccess}
      />,
    );
    fireEvent.click(screen.getByLabelText(/Tôi đã nắm rõ số lượng 4 lượt đặt trước/i));
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận đóng 2 khung giờ/i }));
    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/admin/examination-slots/batch-close',
        expect.objectContaining({ ids: ['101', '102'], slotIds: [101, 102] }),
      );
      expect(onBatchSuccess).toHaveBeenCalled();
    });
  });

  it('closes and reopens slots from ExaminationSlotListPage row actions', async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter>
          <ExaminationSlotListPage />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(await screen.findByText('SLT-TEST-02')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Đóng khung giờ SLT-TEST-02' }));
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận đóng khung giờ/i }));
    expect(await screen.findByText(/đã được đóng thành công/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Mở lại khung giờ SLT-TEST-03' }));
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận mở lại khung giờ/i }));
    expect(await screen.findByText(/đã được mở lại thành công/i)).toBeInTheDocument();
  });
});
