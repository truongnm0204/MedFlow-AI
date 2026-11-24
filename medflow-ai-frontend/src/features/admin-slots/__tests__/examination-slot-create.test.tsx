import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { ExaminationSlotCreatePage } from '../pages/examination-slot-create-page';
import { useExaminationSlotStore } from '../store/examination-slot.store';

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom');
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  };
});

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
  },
}));

describe('ExaminationSlotCreatePage (SCR-ADM-SLT-02 real API, no hardcoded Oct 2026 prefill)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useExaminationSlotStore.setState({
      slots: [],
      draft: null,
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
                specialty: 'Khoa Nội tiết',
                room: 'RM-201',
                capacityBooked: 2,
                capacityMax: 8,
                date: '2026-11-25',
                startTime: '08:00',
                endTime: '08:30',
                status: 'OPEN',
              },
            ],
          },
        };
      }
      if (target.includes('/admin/examination-services')) {
        return {
          data: {
            data: [
              {
                id: 5,
                serviceCode: 'EX-1-01',
                name: 'Khám Nội tiết',
                category: 'Khám Lâm sàng',
                departmentName: 'Khoa Nội tiết',
                price: 300000,
                allowsOnlineBooking: true,
                isActive: true,
                status: 'ACTIVE',
              },
              {
                id: 6,
                serviceCode: 'EX-3-01',
                name: 'Chụp X-quang Ngực thẳng',
                category: 'Cận lâm sàng kỹ thuật',
                departmentName: 'Khoa CĐHA',
                price: 250000,
                allowsOnlineBooking: false,
                isActive: true,
                status: 'ACTIVE',
              },
            ],
          },
        };
      }
      if (target.includes('/admin/doctors')) {
        return {
          data: {
            data: [
              {
                id: 10,
                doctorCode: 'DOC-0010',
                fullName: 'BS. Nguyễn Thu Lan',
                specialtyName: 'Nội tiết',
                departmentName: 'Khoa Nội tiết',
                isActive: true,
              },
            ],
          },
        };
      }
      if (target.includes('/admin/rooms')) {
        return {
          data: {
            data: [
              {
                id: 20,
                roomCode: 'RM-201',
                name: 'Phòng Nội tiết 1',
                departmentName: 'Khoa Nội tiết',
                facility: 'MedFlow Cơ sở 1',
                status: 'ACTIVE',
                isActive: true,
              },
            ],
          },
        };
      }
      return { data: { data: [] } };
    });

    vi.mocked(apiClient.post).mockResolvedValue({
      data: {
        id: 99,
        code: 'SLT-20261125-99',
        doctorId: 10,
        doctorName: 'BS. Nguyễn Thu Lan',
        serviceId: 5,
        serviceName: 'Khám Nội tiết',
        serviceCode: 'EX-1-01',
        serviceType: 'OUTPATIENT',
        specialty: 'Khoa Nội tiết',
        room: 'RM-201',
        capacityBooked: 0,
        capacityMax: 10,
        date: '2026-11-25',
        startTime: '11:00',
        endTime: '11:30',
        status: 'OPEN',
      },
    });
  });

  const renderPage = () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={qc}>
        <MemoryRouter>
          <ExaminationSlotCreatePage />
        </MemoryRouter>
      </QueryClientProvider>,
    );
  };

  it('starts with empty slot code and does NOT prefill hardcoded SLT-HN-20261027-01 or 2026-10-27', () => {
    renderPage();
    expect(
      screen.getByRole('heading', { level: 1, name: 'Tạo khung giờ khám' }),
    ).toBeInTheDocument();
    expect((screen.getByLabelText(/Mã slot/i) as HTMLInputElement).value).toBe('');
    expect(screen.queryByDisplayValue('SLT-HN-20261027-01')).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue('2026-10-27')).not.toBeInTheDocument();
  });

  it('checks doctor schedule conflict, validates endTime > startTime, and submits POST /admin/examination-slots', async () => {
    renderPage();

    expect(await screen.findByText(/Khám Nội tiết - EX-1-01/i)).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/Dịch vụ/i), { target: { value: '5' } });
    fireEvent.change(screen.getByLabelText(/Bác sĩ/i), { target: { value: '10' } });
    fireEvent.change(screen.getByLabelText('Ngày khám'), { target: { value: '2026-11-25' } });
    fireEvent.change(screen.getByLabelText('Giờ bắt đầu'), { target: { value: '08:00' } });
    fireEvent.change(screen.getByLabelText('Giờ kết thúc'), { target: { value: '08:20' } });

    // Check overlapping doctor conflict
    fireEvent.click(screen.getByRole('button', { name: 'Kiểm tra xung đột' }));
    expect(
      screen.getByText(/Xung đột lịch bác sĩ: BS. Nguyễn Thu Lan đã có slot/i),
    ).toBeInTheDocument();

    // Invalid time order
    fireEvent.change(screen.getByLabelText('Giờ bắt đầu'), { target: { value: '09:00' } });
    fireEvent.change(screen.getByLabelText('Giờ kết thúc'), { target: { value: '08:30' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu khung giờ' }));
    expect(await screen.findByText('Giờ kết thúc phải sau giờ bắt đầu')).toBeInTheDocument();
    expect(apiClient.post).not.toHaveBeenCalled();

    // Valid non-conflicting window & submit
    fireEvent.change(screen.getByLabelText(/Mã slot/i), { target: { value: 'SLT-20261125-99' } });
    fireEvent.change(screen.getByLabelText(/Cơ sở áp dụng/i), {
      target: { value: 'MedFlow Cơ sở 1' },
    });
    fireEvent.change(screen.getByLabelText('Giờ bắt đầu'), { target: { value: '11:00' } });
    fireEvent.change(screen.getByLabelText('Giờ kết thúc'), { target: { value: '11:30' } });
    fireEvent.click(screen.getByRole('button', { name: '10 lượt' }));

    fireEvent.click(screen.getByRole('button', { name: 'Lưu khung giờ' }));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/admin/examination-slots',
        expect.objectContaining({
          serviceId: 5,
          doctorId: 10,
          date: '2026-11-25',
          startTime: '11:00',
          endTime: '11:30',
          capacity: 10,
        }),
      );
      expect(mockNavigate).toHaveBeenCalledWith('/admin/slots');
    });
  });
});
