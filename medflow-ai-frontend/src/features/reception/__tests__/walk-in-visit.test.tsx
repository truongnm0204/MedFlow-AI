import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { WalkInVisitPage } from '../pages/walk-in-visit-page';

const catalogDto = {
  hospitalId: 1,
  hospitalName: 'Bệnh viện Demo',
  departments: [
    {
      id: 1,
      code: 'GASTRO',
      name: 'Khoa Tiêu hóa',
      location: 'Tầng 1',
      services: [
        {
          id: 11,
          code: 'DV-TH-01',
          name: 'Khám Chuyên khoa Tiêu Hóa Tự Nguyện',
          price: 250000,
          priceFormatted: '250.000 VNĐ',
        },
      ],
    },
    {
      id: 2,
      code: 'CARDIO',
      name: 'Khoa Tim mạch',
      location: 'Tầng 2',
      services: [
        {
          id: 21,
          code: 'DV-TM-01',
          name: 'Khám Chuyên khoa Tim mạch Tự Nguyện',
          price: 300000,
          priceFormatted: '300.000 VNĐ',
        },
      ],
    },
  ],
  symptomOptions: ['Đau tức ngực'],
};

const searchDto = {
  items: [
    {
      id: 21,
      patientCode: 'BN-2024-0021',
      fullName: 'Trần Minh Đức',
      gender: 'MALE',
      dateOfBirth: '1985-03-02',
      identityCardMasked: '0010***678',
      phone: '0912345678',
      address: 'Hà Nội',
      totalPastVisits: 3,
      todayAppointment: null,
    },
  ],
  totalCount: 1,
  page: 1,
  pageSize: 50,
  counts: { all: 1, hasAppointment: 0, checkedIn: 0, noAppointment: 1 },
};

function renderPage() {
  useAuthStore.setState({
    user: {
      id: 2,
      email: 'letan@medflow.test',
      fullName: 'Nhân viên Lễ tân',
      roles: ['RECEPTION'],
      patientCode: null,
    },
    accessToken: 'tok',
    refreshToken: 'rtok',
    isAuthenticated: true,
  });

  vi.spyOn(apiClient, 'get').mockImplementation(async (url) => {
    if (url === '/reception/walk-in/catalog') return { data: catalogDto } as never;
    if (url === '/reception/patients/search') return { data: searchDto } as never;
    return { data: {} } as never;
  });
  vi.spyOn(apiClient, 'post').mockResolvedValue({
    data: {
      visitId: 11,
      visitCode: 'VIS-2026-0415-0145',
      ticketNo: 'A019',
      patientName: 'Trần Minh Đức',
      departmentName: 'Khoa Tiêu hóa',
      checkedInAt: '09:20',
    },
  } as never);

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/reception/walk-in?patientCode=BN-2024-0021']}>
        <WalkInVisitPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('WalkInVisitPage (Issue #75)', () => {
  it('renders patient demographics loaded through the search API', async () => {
    renderPage();

    expect(await screen.findByText('Trần Minh Đức')).toBeInTheDocument();
    expect(screen.getByText('BN-2024-0021')).toBeInTheDocument();
    expect(screen.getByText(/CCCD đã xác thực/i)).toBeInTheDocument();
  });

  it('renders clinical priority options and updates selection on click', async () => {
    renderPage();

    const priorityBtn = await screen.findByRole('button', {
      name: /Ưu tiên người cao tuổi \/ Thai sản \/ Trẻ nhỏ/i,
    });
    fireEvent.click(priorityBtn);

    expect(priorityBtn).toHaveClass('ring-2');
  });

  it('updates available services when changing department', async () => {
    renderPage();

    const deptSelect = await screen.findByLabelText(/Khoa khám tiếp nhận/i);
    fireEvent.change(deptSelect, { target: { value: '2' } });

    expect(screen.getByText(/Khám Chuyên khoa Tim mạch Tự Nguyện/i)).toBeInTheDocument();
  });

  it('toggles symptoms on click', async () => {
    renderPage();

    const chestPainBtn = await screen.findByRole('button', { name: /Đau tức ngực/i });
    fireEvent.click(chestPainBtn);

    expect(screen.getByRole('button', { name: /Đau tức ngực ✓/i })).toBeInTheDocument();
  });

  it('displays validation error if reason for visit is too short', async () => {
    renderPage();

    const reasonInput = await screen.findByLabelText(/Lý do đến khám/i);
    fireEvent.change(reasonInput, { target: { value: 'Đau' } });

    fireEvent.click(
      screen.getByRole('button', { name: /Tạo lượt khám & Chuyển sang phân buồng/i }),
    );

    await waitFor(() => {
      expect(screen.getByText(/Lý do đến khám phải có ít nhất 5 ký tự/i)).toBeInTheDocument();
    });
  });

  it('submits the walk-in visit through the API and shows the ticket', async () => {
    renderPage();

    const reasonInput = await screen.findByLabelText(/Lý do đến khám/i);
    fireEvent.change(reasonInput, {
      target: { value: 'Đau quặn vùng thượng vị sau ăn 2 ngày nay.' },
    });

    fireEvent.click(
      screen.getByRole('button', { name: /Tạo lượt khám & Chuyển sang phân buồng/i }),
    );

    await waitFor(
      () => {
        expect(apiClient.post).toHaveBeenCalledWith(
          '/reception/visits/walk-in',
          expect.objectContaining({ patientCode: 'BN-2024-0021' }),
        );
      },
      { timeout: 2000 },
    );

    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText(/Đã Tạo Lượt Khám Vãng Lai/i)).toBeInTheDocument();
    expect(screen.getByText('A019')).toBeInTheDocument();
    expect(screen.getByText('VIS-2026-0415-0145')).toBeInTheDocument();
  });
});
