import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { DoctorSearchPage } from '../pages/doctor-search-page';

function renderDoctorSearchPage() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <DoctorSearchPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DoctorSearchPage (SCR-APT-01)', () => {
  beforeEach(() => {
    window.scrollTo = () => {};
  });

  it('renders page header, breadcrumbs, search bar and initial 6 doctor cards', async () => {
    renderDoctorSearchPage();

    expect(screen.getByText('Danh bạ Bác sĩ Chuyên khoa Ngoại trú')).toBeInTheDocument();
    expect(screen.getByText('Danh bạ Bác sĩ (SCR-APT-01)')).toBeInTheDocument();
    expect(screen.getByText(/Cập nhật ca trực:/)).toBeInTheDocument();

    // Await async doctor cards to load
    expect(await screen.findByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();
    expect(screen.getByText('ThS.BS Nguyễn Thu Trang')).toBeInTheDocument();
    expect(screen.getByText('PGS.TS.BS Phạm Hoàng Long')).toBeInTheDocument();
    expect(screen.getByText('BSCKI Lê Minh Đức')).toBeInTheDocument();
    expect(screen.getByText('ThS.BS Vũ Hải Yến')).toBeInTheDocument();
    expect(screen.getByText('BSCKII Đặng Thành Nam')).toBeInTheDocument();

    // Check pagination counter
    expect(screen.getByText(/bác sĩ chuyên khoa sẵn sàng nhận lịch khám/)).toBeInTheDocument();
  });

  it('filters doctors when clicking a specialty tab', async () => {
    renderDoctorSearchPage();

    expect(await screen.findByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();

    const timMachTab = screen.getByRole('button', { name: 'Tim Mạch' });
    fireEvent.click(timMachTab);

    // Dr. Nguyễn Thu Trang is Tim Mạch, Dr. Trần Quốc Tuấn is Tiêu Hóa
    await waitFor(() => {
      expect(screen.queryByText('BS.CKII Trần Quốc Tuấn')).not.toBeInTheDocument();
    });
    expect(screen.getByText('ThS.BS Nguyễn Thu Trang')).toBeInTheDocument();
  });

  it('filters doctors when searching by keyword', async () => {
    renderDoctorSearchPage();

    expect(await screen.findByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();

    const searchInput = screen.getByPlaceholderText(
      'Tìm kiếm theo tên bác sĩ, học hàm, chuyên khoa khám bệnh...',
    );
    fireEvent.change(searchInput, { target: { value: 'Phạm Hoàng Long' } });

    const searchButton = screen.getByRole('button', { name: 'Tìm kiếm' });
    fireEvent.click(searchButton);

    await waitFor(() => {
      expect(screen.queryByText('BS.CKII Trần Quốc Tuấn')).not.toBeInTheDocument();
    });
    expect(screen.getByText('PGS.TS.BS Phạm Hoàng Long')).toBeInTheDocument();
  });

  it('filters doctors when toggling Có lịch hôm nay', async () => {
    renderDoctorSearchPage();

    expect(await screen.findByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();

    const todayButton = screen.getByRole('button', {
      name: /Có lịch hôm nay/i,
    });
    fireEvent.click(todayButton);

    await waitFor(() => {
      expect(screen.queryByText('ThS.BS Nguyễn Thu Trang')).not.toBeInTheDocument();
    });
    expect(screen.getByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();
  });

  it('shows empty state when no doctors match the search query', async () => {
    renderDoctorSearchPage();

    expect(await screen.findByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();

    const searchInput = screen.getByPlaceholderText(
      'Tìm kiếm theo tên bác sĩ, học hàm, chuyên khoa khám bệnh...',
    );
    fireEvent.change(searchInput, {
      target: { value: 'Không tìm thấy bác sĩ nào xyz123' },
    });

    const searchButton = screen.getByRole('button', { name: 'Tìm kiếm' });
    fireEvent.click(searchButton);

    expect(await screen.findByText('Không tìm thấy bác sĩ phù hợp')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Đặt lại toàn bộ bộ lọc' })).toBeInTheDocument();
  });

  it('resets filters when clicking Xóa tất cả bộ lọc', async () => {
    renderDoctorSearchPage();

    expect(await screen.findByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();

    // Apply a specialty filter
    const nhiKhoaTab = screen.getByRole('button', { name: 'Nhi Khoa' });
    fireEvent.click(nhiKhoaTab);

    await waitFor(() => {
      expect(screen.queryByText('BS.CKII Trần Quốc Tuấn')).not.toBeInTheDocument();
    });
    expect(screen.getByText('ThS.BS Vũ Hải Yến')).toBeInTheDocument();

    // Click reset
    const resetButton = screen.getByRole('button', {
      name: /Xóa tất cả bộ lọc/i,
    });
    fireEvent.click(resetButton);

    await waitFor(() => {
      expect(screen.getByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();
    });
  });
});
