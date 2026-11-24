import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { INITIAL_EXAMINATION_SLOTS } from '../data/examination-slots-mock';
import { ExaminationSlotListPage } from '../pages/examination-slot-list-page';
import { useExaminationSlotStore } from '../store/examination-slot.store';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
  },
}));

describe('ExaminationSlotListPage (SCR-ADM-SLT-01 real API)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useExaminationSlotStore.setState({
      slots: [],
      searchQuery: '',
      dateFilter: '',
      specialtyFilter: '',
      statusFilter: '',
      page: 1,
      pageSize: 20,
      selectedIds: [],
    });

    vi.mocked(apiClient.get).mockResolvedValue({
      data: {
        data: INITIAL_EXAMINATION_SLOTS.map((s, idx) => ({
          id: idx + 1,
          code: s.code,
          doctorId: idx + 1,
          doctorName: s.doctorName,
          serviceId: idx + 1,
          serviceName: s.serviceName,
          serviceCode: s.serviceCode,
          serviceType: s.serviceType,
          specialty: s.specialty,
          room: s.room,
          capacityBooked: s.capacityBooked,
          capacityMax: s.capacityMax,
          date: s.date,
          dateDisplay: s.dateDisplay,
          startTime: s.startTime,
          endTime: s.endTime,
          status: s.status,
        })),
      },
    });
    vi.mocked(apiClient.post).mockResolvedValue({ data: { closedCount: 2 } });
  });

  const renderPage = () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={qc}>
        <MemoryRouter>
          <ExaminationSlotListPage />
        </MemoryRouter>
      </QueryClientProvider>,
    );
  };

  it('fetches GET /admin/examination-slots and renders header, 4 metric cards, and table rows', async () => {
    renderPage();

    expect(await screen.findByText('SLT-001')).toBeInTheDocument();
    expect(screen.getByText('SLOT ĐANG MỞ')).toBeInTheDocument();
    expect(screen.getByText('CÓ LƯỢT ĐẶT')).toBeInTheDocument();
    expect(screen.getByText('SẮP ĐẾN GIỜ')).toBeInTheDocument();
    expect(screen.getByText('CẦN RÀ SOÁT')).toBeInTheDocument();
    expect(screen.getAllByText('BS. Nguyễn Thu Lan').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('SLT-002')).toBeInTheDocument();
  });

  it('filters slot items by search, specialty, and status, changes pageSize, and exports CSV', async () => {
    renderPage();
    expect(await screen.findByText('SLT-001')).toBeInTheDocument();

    const searchInput = screen.getByLabelText('Tìm kiếm khung giờ khám');
    fireEvent.change(searchInput, { target: { value: 'SLT-002' } });
    expect(screen.getByText('SLT-002')).toBeInTheDocument();
    expect(screen.queryByText('SLT-001')).not.toBeInTheDocument();

    fireEvent.change(searchInput, { target: { value: '' } });
    fireEvent.change(screen.getByLabelText('Lọc theo chuyên khoa'), {
      target: { value: 'Tim mạch' },
    });
    expect(screen.getAllByText('BS. Trần Hoài Nam').length).toBeGreaterThanOrEqual(1);

    fireEvent.change(screen.getByLabelText('Lọc theo chuyên khoa'), { target: { value: '' } });
    fireEvent.change(screen.getByLabelText('Lọc theo trạng thái'), {
      target: { value: 'CLOSED' },
    });
    expect(screen.getByText('SLT-008')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Số dòng trên mỗi trang'), {
      target: { value: '8' },
    });
    expect(useExaminationSlotStore.getState().pageSize).toBe(8);

    const createObjectURLMock = vi.fn().mockReturnValue('blob:test');
    window.URL.createObjectURL = createObjectURLMock;
    window.URL.revokeObjectURL = vi.fn();
    fireEvent.click(screen.getByRole('button', { name: /Xuất Excel/i }));
    expect(createObjectURLMock).toHaveBeenCalled();
  });

  it('opens batch close modal when selecting rows and clicking Đóng hàng loạt', async () => {
    renderPage();
    expect(await screen.findByText('SLT-001')).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Chọn tất cả khung giờ trên trang'));
    fireEvent.click(screen.getByRole('button', { name: 'Đóng hàng loạt' }));

    await waitFor(() => {
      expect(screen.getByText('Đóng Hàng Loạt Khung Giờ Khám')).toBeInTheDocument();
    });
  });
});
