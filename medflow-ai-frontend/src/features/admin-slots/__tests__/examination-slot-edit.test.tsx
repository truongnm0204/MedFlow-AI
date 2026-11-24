import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { SlotEditModal } from '../pages/slot-edit-modal';
import { useExaminationSlotStore } from '../store/examination-slot.store';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    put: vi.fn(),
  },
}));

describe('SlotEditModal (SCR-ADM-SLT-03 real API)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useExaminationSlotStore.setState({
      slots: [],
      selectedIds: [],
    });

    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      const target = String(url);
      if (target.includes('/admin/examination-slots')) {
        return {
          data: {
            data: [
              {
                id: 1,
                code: 'SLT-001',
                doctorId: 10,
                doctorName: 'BS. Nguyễn Thu Lan',
                serviceId: 5,
                serviceName: 'Khám Nội tiết',
                serviceCode: 'EX-1-01',
                serviceType: 'OUTPATIENT',
                specialty: 'Nội tiết',
                room: 'PK-102',
                capacityBooked: 4,
                capacityMax: 8,
                date: '2026-11-27',
                startTime: '08:00',
                endTime: '08:30',
                status: 'OPEN',
                facility: 'MedFlow Cơ sở 1',
              },
            ],
          },
        };
      }
      if (target.includes('/admin/rooms')) {
        return {
          data: {
            data: [
              { id: 1, roomCode: 'PK-102', name: 'Phòng 102', status: 'ACTIVE', isActive: true },
              { id: 2, roomCode: 'PK-201', name: 'Phòng 201', status: 'ACTIVE', isActive: true },
            ],
          },
        };
      }
      return { data: { data: [] } };
    });

    vi.mocked(apiClient.put).mockImplementation(async (_url, body) => {
      const payload = body as { roomCode?: string; capacity?: number };
      return {
        data: {
          id: 1,
          code: 'SLT-001',
          doctorId: 10,
          doctorName: 'BS. Nguyễn Thu Lan',
          serviceId: 5,
          serviceName: 'Khám Nội tiết',
          serviceCode: 'EX-1-01',
          serviceType: 'OUTPATIENT',
          specialty: 'Nội tiết',
          room: payload.roomCode ?? 'PK-102',
          capacityBooked: 4,
          capacityMax: payload.capacity ?? 8,
          date: '2026-11-27',
          startTime: '08:00',
          endTime: '08:30',
          status: 'OPEN',
        },
      };
    });
  });

  const renderModal = (slotId = '1') => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={[`/admin/slots/${slotId}/edit`]}>
          <Routes>
            <Route path="/admin/slots" element={<div>Danh sách Khung giờ Slot Mock</div>} />
            <Route path="/admin/slots/:slotId/edit" element={<SlotEditModal />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );
  };

  it('loads slot from GET /admin/examination-slots and shows not-found for invalid ID', async () => {
    const first = renderModal('SLT-001');
    expect(
      await screen.findByRole('heading', { name: /Khung giờ Khám - SLT-001/i }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText(/Mã Slot khám \(Slot ID\)/i)).toHaveValue('SLT-001');
    first.unmount();

    renderModal('SLT-INVALID-999');
    expect(await screen.findByText('Không tìm thấy thông tin khung giờ khám.')).toBeInTheDocument();
  });

  it('validates time order and calls PUT /admin/examination-slots/:id on valid save', async () => {
    renderModal('SLT-001');
    expect(await screen.findByDisplayValue('SLT-001')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/Giờ kết thúc/i), { target: { value: '07:30' } });
    fireEvent.click(screen.getByRole('button', { name: /Lưu khung giờ khám/i }));
    expect(await screen.findByText('Giờ kết thúc phải sau giờ bắt đầu')).toBeInTheDocument();
    expect(apiClient.put).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText(/Giờ kết thúc/i), { target: { value: '08:30' } });
    fireEvent.change(screen.getByLabelText(/Phòng khám \/ Buồng khám/i), {
      target: { value: 'PK-201' },
    });
    fireEvent.click(screen.getByRole('button', { name: '12' }));
    fireEvent.click(screen.getByRole('button', { name: /Lưu khung giờ khám/i }));

    await waitFor(() => {
      expect(apiClient.put).toHaveBeenCalledWith(
        '/admin/examination-slots/1',
        expect.objectContaining({
          roomCode: 'PK-201',
          capacity: 12,
        }),
      );
      expect(screen.getByText('Danh sách Khung giờ Slot Mock')).toBeInTheDocument();
    });
  });
});
