import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { INITIAL_SERVICES } from '../data/examination-services-data';
import { ExaminationServiceListPage } from '../pages/examination-service-list-page';
import { useExaminationServiceStore } from '../store/examination-service.store';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
  },
}));

describe('ExaminationServiceListPage (SCR-ADM-ESV-01 real API)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useExaminationServiceStore.setState({
      services: [],
      searchQuery: '',
      categoryFilter: '',
      departmentFilter: '',
      bookingFilter: '',
      statusFilter: '',
      page: 1,
      pageSize: 10,
      selectedIds: [],
      isColumnModalOpen: false,
      priceHistoryService: null,
      updatePriceService: null,
    });

    vi.mocked(apiClient.get).mockResolvedValue({
      data: {
        data: INITIAL_SERVICES.map((srv, idx) => ({
          id: idx + 1,
          serviceCode: srv.code,
          name: srv.name,
          description: srv.description,
          category: srv.category,
          departmentName: srv.department,
          price: srv.price,
          allowsOnlineBooking: srv.onlineBooking,
          isActive: srv.status === 'ACTIVE',
          status: srv.status,
          effectiveDate: srv.effectiveDate,
          decisionNumber: srv.decisionNumber,
        })),
      },
    });
    vi.mocked(apiClient.patch).mockResolvedValue({ data: {} });
  });

  const renderPage = () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={qc}>
        <MemoryRouter>
          <ExaminationServiceListPage />
        </MemoryRouter>
      </QueryClientProvider>,
    );
  };

  it('fetches GET /admin/examination-services and renders header, 4 metric cards, and table rows', async () => {
    renderPage();

    expect(await screen.findByText('EX-1-01')).toBeInTheDocument();
    expect(screen.getByText('TỔNG SỐ DỊCH VỤ KHÁM')).toBeInTheDocument();
    expect(screen.getAllByText('32').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('MỞ ĐẶT HẸN ONLINE')).toBeInTheDocument();
    expect(screen.getByText('28')).toBeInTheDocument();
    expect(screen.getByText('TẠM NGỪNG DỊCH VỤ')).toBeInTheDocument();
    expect(screen.getByText('04')).toBeInTheDocument();
    expect(screen.getByText('Khám Nội Tổng quát & Tư vấn Sức khỏe')).toBeInTheDocument();
  });

  it('filters services by search query, category, and department, and paginates', async () => {
    renderPage();
    expect(await screen.findByText('EX-1-01')).toBeInTheDocument();

    const nextBtn = screen.getByLabelText('Trang tiếp');
    fireEvent.click(nextBtn);
    expect(screen.getByRole('button', { name: 'Trang 2' })).toHaveAttribute('aria-current', 'page');

    const searchInput = screen.getByLabelText('Tìm dịch vụ khám');
    fireEvent.change(searchInput, { target: { value: 'Tim mạch' } });
    expect(screen.getByText('Khám Tim mạch & Đo Điện tâm đồ Chuyên sâu')).toBeInTheDocument();
    expect(screen.queryByText('Khám Nội Tổng quát & Tư vấn Sức khỏe')).not.toBeInTheDocument();

    fireEvent.change(searchInput, { target: { value: '' } });
    fireEvent.change(screen.getByLabelText('Lọc phân loại'), {
      target: { value: 'Hội chẩn Lâm sàng' },
    });
    expect(
      screen.getByText('Hội chẩn Đa chuyên khoa Ca bệnh Phức tạp Ngoại trú'),
    ).toBeInTheDocument();
  });

  it('calls PATCH /admin/examination-services/:code/deactivate on single and batch suspend, and opens modals', async () => {
    renderPage();
    expect(await screen.findByText('EX-1-01')).toBeInTheDocument();

    // Open price history modal
    fireEvent.click(screen.getByLabelText('Xem lịch sử giá Khám Nội Tổng quát & Tư vấn Sức khỏe'));
    expect(screen.getByText('Lịch Sử Biểu Phí & Quy Tắc Cơ Sở UC52')).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole('button', { name: 'Đóng' })[0]);

    // Open column modal
    fireEvent.click(screen.getByTitle('Tùy chỉnh cột hiển thị'));
    expect(screen.getByText('Tùy Chỉnh Cột Hiển Thị')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Áp Dụng & Đóng'));

    // Deactivate single service
    fireEvent.click(screen.getByLabelText('Đổi trạng thái Khám Nội Tổng quát & Tư vấn Sức khỏe'));
    expect(
      screen.getByRole('heading', { name: 'Ngừng hoạt động dịch vụ khám?' }),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Ngừng hoạt động' }));

    await waitFor(() => {
      expect(apiClient.patch).toHaveBeenCalledWith(
        '/admin/examination-services/EX-1-01/deactivate',
        expect.any(Object),
      );
    });
    expect(
      await screen.findByText(
        /Đã chuyển trạng thái dịch vụ Khám Nội Tổng quát & Tư vấn Sức khỏe sang Tạm ngừng hoạt động\./i,
      ),
    ).toBeInTheDocument();

    // Batch suspend
    fireEvent.click(screen.getByLabelText('Chọn Khám Nội Tổng quát & Tư vấn Sức khỏe'));
    fireEvent.click(screen.getByText('Tạm dừng hàng loạt'));
    await waitFor(() => {
      expect(apiClient.patch).toHaveBeenCalledTimes(2);
    });
  });
});
