import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { ExaminationServiceCatalogPage } from '../pages/examination-service-catalog-page';

function renderExaminationServiceCatalogPage() {
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
        <ExaminationServiceCatalogPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('ExaminationServiceCatalogPage (SCR-APT-03)', () => {
  beforeEach(() => {
    window.scrollTo = () => {};
  });

  it('renders page header, breadcrumbs, search bar and initial 6 examination services', async () => {
    renderExaminationServiceCatalogPage();

    expect(screen.getByText('Danh mục Dịch vụ')).toBeInTheDocument();
    expect(screen.getByText('Danh mục Dịch vụ Khám (SCR-APT-03)')).toBeInTheDocument();

    // Verify initial services appear
    expect(await screen.findByText('Khám Chuyên khoa Tiêu Hóa')).toBeInTheDocument();
    expect(screen.getByText('Khám Chuyên khoa Tim Mạch')).toBeInTheDocument();
    expect(screen.getByText('Khám Nội Tổng Quát Ban Đầu')).toBeInTheDocument();
    expect(screen.getByText('Khám Tai Mũi Họng')).toBeInTheDocument();
    expect(screen.getByText('Khám Chuyên khoa Nhi Tự Nguyện')).toBeInTheDocument();
    expect(screen.getByText('Khám Chuyên khoa Cơ Xương Khớp')).toBeInTheDocument();

    // Check pagination summary
    expect(screen.getByText(/dịch vụ khám ngoại trú/)).toBeInTheDocument();
  });

  it('filters services when selecting a category tab', async () => {
    renderExaminationServiceCatalogPage();

    expect(await screen.findByText('Khám Chuyên khoa Tiêu Hóa')).toBeInTheDocument();

    const packageTab = screen.getByRole('button', { name: 'Gói Tầm soát Toàn diện' });
    fireEvent.click(packageTab);

    // Gói khám tầm soát should appear
    expect(await screen.findByText('Gói Tầm soát Sức khỏe Toàn diện Cơ bản')).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.queryByText('Khám Chuyên khoa Tiêu Hóa')).not.toBeInTheDocument();
    });
  });

  it('filters services when typing in search bar', async () => {
    renderExaminationServiceCatalogPage();

    expect(await screen.findByText('Khám Chuyên khoa Tiêu Hóa')).toBeInTheDocument();

    const searchInput = screen.getByPlaceholderText(
      'Tìm theo tên dịch vụ khám, gói khám, bác sĩ hoặc khoa phòng...',
    );
    fireEvent.change(searchInput, { target: { value: 'Tai Mũi Họng' } });

    const searchBtn = screen.getByRole('button', { name: 'Tìm kiếm' });
    fireEvent.click(searchBtn);

    expect(await screen.findByText('Khám Tai Mũi Họng')).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.queryByText('Khám Chuyên khoa Tim Mạch')).not.toBeInTheDocument();
    });
  });

  it('opens detail modal when clicking Chi tiết dịch vụ', async () => {
    renderExaminationServiceCatalogPage();

    expect(await screen.findByText('Khám Chuyên khoa Tiêu Hóa')).toBeInTheDocument();

    const detailButtons = screen.getAllByRole('button', { name: 'Chi tiết dịch vụ' });
    fireEvent.click(detailButtons[0]);

    // Modal dialog opens
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText('Mô tả dịch vụ')).toBeInTheDocument();
    expect(screen.getByText('Lưu ý chuẩn bị trước khi khám')).toBeInTheDocument();

    // Close modal
    const closeBtn = screen.getByRole('button', { name: 'Đóng' });
    fireEvent.click(closeBtn);

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });
});
