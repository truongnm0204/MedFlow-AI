import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';
import { ServicePricingPage } from '../pages/service-pricing-page';
import { useExaminationServiceStore } from '../store/examination-service.store';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    put: vi.fn(),
  },
}));

const MOCK_API_SERVICES = [
  {
    id: 1,
    serviceCode: 'EX-1-01',
    name: 'Khám Nội Tổng quát & Tư vấn Sức khỏe',
    description: 'Khám sức khỏe tổng quát',
    category: 'Khám Lâm sàng',
    departmentName: 'Khoa Khám bệnh',
    price: 300000,
    hasEffectivePrice: true,
    allowsOnlineBooking: true,
    isActive: true,
    status: 'ACTIVE',
  },
  {
    id: 2,
    serviceCode: 'EX-2-02',
    name: 'Hội chẩn Chuyên gia Tim mạch',
    description: 'Hội chẩn chuyên sâu',
    category: 'Hội chẩn Lâm sàng',
    departmentName: 'Khoa Tim mạch',
    price: 550000,
    hasEffectivePrice: true,
    allowsOnlineBooking: true,
    isActive: true,
    status: 'ACTIVE',
  },
];

describe('ServicePricingPage (UC52 / GH-818 GET /admin/service-prices)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useAuthStore.setState({
      isAuthenticated: true,
      user: {
        id: 1,
        fullName: 'Quản trị Bệnh viện',
        email: 'admin@medflow.vn',
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
      },
    });
    useExaminationServiceStore.setState({
      services: [],
      priceHistoryService: null,
      updatePriceService: null,
    });
    vi.mocked(apiClient.get).mockResolvedValue({
      data: { data: MOCK_API_SERVICES },
    });
  });

  const renderPage = () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/pricing']}>
          <ServicePricingPage />
        </MemoryRouter>
      </QueryClientProvider>,
    );
  };

  it('fetches GET /admin/service-prices and shows Chưa cập nhật when decisionNumber is null', async () => {
    renderPage();

    expect(await screen.findByText('EX-1-01')).toBeInTheDocument();
    expect(apiClient.get).toHaveBeenCalledWith(
      '/admin/service-prices',
      expect.objectContaining({ params: { page: 1, pageSize: 200 } }),
    );
    expect(screen.getByText('TỔNG DỊCH VỤ ĐỊNH GIÁ')).toBeInTheDocument();
    expect(screen.getByText('300.000 đ')).toBeInTheDocument();
    expect(screen.queryByText('QĐ-142/BV-KH')).not.toBeInTheDocument();
    expect(screen.getAllByText('Chưa cập nhật').length).toBeGreaterThan(0);
  });

  it('filters services by search & price range, opens modals, and enforces RBAC for DOCTOR', async () => {
    const first = renderPage();
    expect(await screen.findByText('EX-1-01')).toBeInTheDocument();

    const searchInput = screen.getByPlaceholderText(/Tìm mã hoặc tên dịch vụ khám/i);
    fireEvent.change(searchInput, { target: { value: 'EX-1-01' } });
    expect(screen.queryByText('EX-2-02')).not.toBeInTheDocument();

    fireEvent.click(screen.getAllByRole('button', { name: /Đặt lại bộ lọc/i })[0]);
    expect(screen.getByText('EX-2-02')).toBeInTheDocument();

    fireEvent.click(screen.getAllByRole('button', { name: /Cập nhật giá/i })[0]);
    expect(screen.getByText('Cập nhật Biểu phí Dịch vụ (UC52)')).toBeInTheDocument();
    useExaminationServiceStore.setState({ updatePriceService: null });
    first.unmount();

    useAuthStore.setState({
      isAuthenticated: true,
      user: {
        id: 99,
        fullName: 'Bác sĩ Khám',
        email: 'doctor@medflow.vn',
        roles: ['DOCTOR'],
        patientCode: null,
      },
    });
    renderPage();
    expect(await screen.findByText('EX-1-01')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: /Cập nhật giá/i })).not.toBeInTheDocument();
    });
  });
});
