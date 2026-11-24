import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { ExaminationServiceCreatePage } from '../pages/examination-service-create-page';
import { useExaminationServiceStore } from '../store/examination-service.store';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    post: vi.fn(),
  },
}));

describe('ExaminationServiceCreatePage (SCR-ADM-ESV-02 real API, no 300k prefill)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useExaminationServiceStore.setState({
      services: [],
      draft: null,
    });
    vi.mocked(apiClient.post).mockResolvedValue({
      data: {
        id: 99,
        serviceCode: 'EX-9-99',
        name: 'Khám Nội Chuyên Sâu',
        category: 'Khám Lâm sàng',
        departmentName: 'Khoa Khám bệnh',
        price: 0,
        allowsOnlineBooking: true,
        isActive: true,
        status: 'ACTIVE',
      },
    });
  });

  const renderComponent = () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/services/new']}>
          <Routes>
            <Route path="/admin/services/new" element={<ExaminationServiceCreatePage />} />
            <Route path="/admin/services" element={<div>Danh mục Dịch vụ Page Mock</div>} />
            <Route
              path="/admin/policies"
              element={<div>Màn hình Chính sách đặt lịch (UC54)</div>}
            />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );
  };

  it('renders empty initial code/name fields (no EX-2-09 prefill) and UC52/UC54 context cards', () => {
    renderComponent();

    expect(
      screen.getByRole('heading', { name: /Thêm mới Dịch vụ Khám Ngoại trú/i }),
    ).toBeInTheDocument();
    const codeInput = screen.getByPlaceholderText('EX-2-09') as HTMLInputElement;
    expect(codeInput.value).toBe('');
    const nameInput = screen.getByPlaceholderText(
      'Khám Chuyên khoa Nội Tiêu hóa & Gan Mật Chuyên sâu',
    ) as HTMLInputElement;
    expect(nameInput.value).toBe('');

    expect(screen.getByText('Giá dịch vụ theo cơ sở')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Mở chính sách đặt lịch/i }));
    expect(screen.getByText('Màn hình Chính sách đặt lịch (UC54)')).toBeInTheDocument();
  });

  it('validates code pattern and name length before calling POST /admin/examination-services', async () => {
    renderComponent();

    fireEvent.change(screen.getByPlaceholderText('EX-2-09'), { target: { value: 'INVALID_CODE' } });
    fireEvent.click(screen.getByRole('button', { name: /Tạo dịch vụ nháp/i }));

    await waitFor(() => {
      expect(screen.getByText(/Mã dịch vụ theo chuẩn EX-x-xx/i)).toBeInTheDocument();
      expect(screen.getByText(/Tên dịch vụ phải có ít nhất 3 ký tự/i)).toBeInTheDocument();
    });
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it('submits POST /admin/examination-services without hardcoded 300000 price and surfaces backend conflict error', async () => {
    const first = renderComponent();

    fireEvent.change(screen.getByPlaceholderText('EX-2-09'), { target: { value: 'EX-9-99' } });
    fireEvent.change(
      screen.getByPlaceholderText('Khám Chuyên khoa Nội Tiêu hóa & Gan Mật Chuyên sâu'),
      { target: { value: 'Khám Nội Chuyên Sâu' } },
    );
    fireEvent.click(screen.getByRole('button', { name: /Lưu bản nháp/i }));
    expect(useExaminationServiceStore.getState().draft?.code).toBe('EX-9-99');

    fireEvent.click(screen.getByRole('button', { name: /Tạo dịch vụ nháp/i }));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/admin/examination-services',
        expect.not.objectContaining({ price: 300000 }),
      );
      expect(screen.getByText('Danh mục Dịch vụ Page Mock')).toBeInTheDocument();
    });
    expect(useExaminationServiceStore.getState().draft).toBeNull();
    first.unmount();

    // Conflict error
    vi.mocked(apiClient.post).mockRejectedValueOnce(
      new Error('Mã dịch vụ "EX-1-01" đã tồn tại trong danh mục.'),
    );
    renderComponent();
    fireEvent.change(screen.getByPlaceholderText('EX-2-09'), { target: { value: 'EX-1-01' } });
    fireEvent.change(
      screen.getByPlaceholderText('Khám Chuyên khoa Nội Tiêu hóa & Gan Mật Chuyên sâu'),
      { target: { value: 'Khám Nội Tổng quát' } },
    );
    fireEvent.click(screen.getByRole('button', { name: /Tạo dịch vụ nháp/i }));

    await waitFor(() => {
      expect(screen.getByText(/Mã dịch vụ "EX-1-01" đã tồn tại/i)).toBeInTheDocument();
    });
  });
});
