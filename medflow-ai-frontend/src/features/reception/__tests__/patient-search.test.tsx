import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { PatientSearchEmptyState } from '../components/patient-search-empty-state';
import { PatientSearchFilterBar } from '../components/patient-search-filter-bar';
import { PatientSearchResultTable } from '../components/patient-search-result-table';
import { PATIENT_SEARCH_MOCK_DATA } from '../data/patient-search-mock';
import { PatientSearchPage } from '../pages/patient-search-page';

const defaultItems = [
  {
    id: 1,
    patientCode: 'BN-2024-0018',
    fullName: 'Nguyễn Văn An',
    gender: 'MALE',
    dateOfBirth: '1988-05-14',
    identityCardMasked: '0010***345',
    phone: '0912345678',
    address: 'Hà Nội',
    totalPastVisits: 6,
    todayAppointment: {
      id: 12,
      time: '09:15',
      doctorName: 'BS. CKII Trần Khánh',
      department: 'Nội tổng quát',
      roomName: 'P.102',
      status: 'BOOKED',
    },
  },
];

afterEach(() => {
  vi.restoreAllMocks();
});

function renderSearchPage(items: unknown[] = defaultItems) {
  vi.spyOn(apiClient, 'get').mockResolvedValue({
    data: {
      items,
      totalCount: items.length,
      page: 1,
      pageSize: 50,
      counts: { all: 6, hasAppointment: 4, checkedIn: 1, noAppointment: 2 },
    },
  } as never);

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <PatientSearchPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('Patient Search Components (Issue #63)', () => {
  describe('PatientSearchFilterBar', () => {
    it('renders input and all 4 tabs with counts', () => {
      const counts = { all: 6, hasAppointment: 4, checkedIn: 1, noAppointment: 2 };
      render(
        <PatientSearchFilterBar
          query=""
          onQueryChange={vi.fn()}
          activeTab="ALL"
          onTabChange={vi.fn()}
          counts={counts}
        />,
      );

      expect(screen.getByPlaceholderText(/Tra cứu theo Mã BN/i)).toBeInTheDocument();
      expect(screen.getByRole('tab', { name: /Tất cả hồ sơ/i })).toBeInTheDocument();
      expect(screen.getByRole('tab', { name: /Có hẹn hôm nay/i })).toBeInTheDocument();
      expect(screen.getByRole('tab', { name: /Đã check-in/i })).toBeInTheDocument();
      expect(screen.getByRole('tab', { name: /Chưa có lịch hẹn/i })).toBeInTheDocument();
    });

    it('triggers onQueryChange when user types and clears query', () => {
      const handleQueryChange = vi.fn();
      render(
        <PatientSearchFilterBar
          query="Nguyễn"
          onQueryChange={handleQueryChange}
          activeTab="ALL"
          onTabChange={vi.fn()}
          counts={{ all: 6, hasAppointment: 4, checkedIn: 1, noAppointment: 2 }}
        />,
      );

      const clearBtn = screen.getByRole('button', { name: /Xóa từ khóa tìm kiếm/i });
      fireEvent.click(clearBtn);

      expect(handleQueryChange).toHaveBeenCalledWith('');
    });
  });

  describe('PatientSearchResultTable', () => {
    it('renders patient rows with code, identity and today appointment info', () => {
      render(<PatientSearchResultTable patients={PATIENT_SEARCH_MOCK_DATA} />);

      expect(screen.getByText('BN-2024-0018')).toBeInTheDocument();
      expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
      expect(screen.getByText('001088012345')).toBeInTheDocument();
      expect(screen.getByText('09:15')).toBeInTheDocument();
      expect(screen.getAllByText(/Không có lịch hẹn/i).length).toBeGreaterThan(0);
    });

    it('calls callbacks when action buttons are clicked', () => {
      const handleSelect = vi.fn();
      const handleCheckIn = vi.fn();
      render(
        <PatientSearchResultTable
          patients={PATIENT_SEARCH_MOCK_DATA}
          onSelectPatient={handleSelect}
          onCheckInPatient={handleCheckIn}
        />,
      );

      const checkInBtns = screen.getAllByRole('button', { name: /Tiếp nhận/i });
      fireEvent.click(checkInBtns[0]);
      expect(handleCheckIn).toHaveBeenCalled();

      const viewBtns = screen.getAllByRole('button', { name: /Xem hồ sơ/i });
      fireEvent.click(viewBtns[0]);
      expect(handleSelect).toHaveBeenCalled();
    });
  });

  describe('PatientSearchEmptyState', () => {
    it('renders search query and calls onClear on button click', () => {
      const handleClear = vi.fn();
      render(<PatientSearchEmptyState query="999999" onClear={handleClear} />);

      expect(screen.getByText(/"999999"/)).toBeInTheDocument();
      expect(
        screen.getByText(/Không tìm thấy bệnh nhân nào khớp với từ khóa/i),
      ).toBeInTheDocument();

      const clearBtn = screen.getByRole('button', { name: /Xóa bộ lọc/i });
      fireEvent.click(clearBtn);
      expect(handleClear).toHaveBeenCalled();
    });
  });

  describe('PatientSearchPage Integration', () => {
    it('renders patients returned by the search API', async () => {
      renderSearchPage();

      expect(await screen.findByText('BN-2024-0018')).toBeInTheDocument();
      expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
      expect(screen.getByText('09:15')).toBeInTheDocument();
    });

    it('requests the checked-in tab from the API', async () => {
      renderSearchPage();
      await screen.findByText('BN-2024-0018');

      fireEvent.click(screen.getByRole('tab', { name: /Đã check-in/i }));

      await waitFor(() => {
        expect(apiClient.get).toHaveBeenCalledWith(
          '/reception/patients/search',
          expect.objectContaining({
            params: expect.objectContaining({ tab: 'CHECKED_IN' }),
          }),
        );
      });
    });

    it('shows empty state when the API returns no patient', async () => {
      renderSearchPage([]);

      expect(
        await screen.findByText(/Không tìm thấy bệnh nhân nào khớp với từ khóa/i),
      ).toBeInTheDocument();
    });
  });
});
