import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { PatientSearchEmptyState } from '../components/patient-search-empty-state';
import { PatientSearchFilterBar } from '../components/patient-search-filter-bar';
import { PatientSearchResultTable } from '../components/patient-search-result-table';
import { PATIENT_SEARCH_MOCK_DATA } from '../data/patient-search-mock';
import { PatientSearchPage } from '../pages/patient-search-page';

function renderSearchPage() {
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
    it('filters patient list in real-time by search query', () => {
      renderSearchPage();

      expect(screen.getByText('BN-2024-0018')).toBeInTheDocument();
      expect(screen.getByText('Trần Thị Bích')).toBeInTheDocument();

      const input = screen.getByPlaceholderText(/Tra cứu theo Mã BN/i);
      fireEvent.change(input, { target: { value: 'Trần Thị Bích' } });

      expect(screen.getByText('Trần Thị Bích')).toBeInTheDocument();
      expect(screen.queryByText('BN-2024-0018')).not.toBeInTheDocument();
    });

    it('filters patient list by appointment tab', () => {
      renderSearchPage();

      const hasApptTab = screen.getByRole('tab', { name: /Có hẹn hôm nay/i });
      fireEvent.click(hasApptTab);

      // Nguyễn Văn An (apt-001) is booked
      expect(screen.getByText('BN-2024-0018')).toBeInTheDocument();
      // Lê Minh Cường has no appointment
      expect(screen.queryByText('Lê Minh Cường')).not.toBeInTheDocument();
    });

    it('shows empty state when no patient matches', () => {
      renderSearchPage();

      const input = screen.getByPlaceholderText(/Tra cứu theo Mã BN/i);
      fireEvent.change(input, { target: { value: 'XYZ-NOT-FOUND' } });

      expect(
        screen.getByText(/Không tìm thấy bệnh nhân nào khớp với từ khóa/i),
      ).toBeInTheDocument();
    });
  });
});
