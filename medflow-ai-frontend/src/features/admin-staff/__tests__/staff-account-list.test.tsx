import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { StaffAccountListPage } from '../pages/staff-account-list-page';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    patch: vi.fn(),
  },
}));

const MOCK_STAFF_ITEMS = Array.from({ length: 10 }, (_, idx) => {
  if (idx === 0) {
    return {
      staffId: 142,
      code: 'STF-0142',
      fullName: 'Lê Hoàng Minh',
      email: 'minh.le@medflow.vn',
      roleCode: 'DOCTOR',
      role: 'Bác sĩ',
      department: 'Khoa Nội Tiêu hóa',
      facility: 'MedFlow Cơ sở 1 - Hà Nội',
      status: 'ACTIVE',
    };
  }
  if (idx === 1) {
    return {
      staffId: 89,
      code: 'STF-0089',
      fullName: 'Trần Hoài Nam',
      email: 'nam.tran@medflow.vn',
      roleCode: 'DOCTOR',
      role: 'Bác sĩ',
      department: 'Khoa Tim mạch',
      facility: 'MedFlow Cơ sở 1 - Hà Nội',
      status: 'ACTIVE',
    };
  }
  if (idx === 2) {
    return {
      staffId: 231,
      code: 'STF-0231',
      fullName: 'Phạm Thu Hà',
      email: 'ha.pham@medflow.vn',
      roleCode: 'RECEPTIONIST',
      role: 'Nhân viên quầy',
      department: 'Khoa Khám bệnh',
      facility: 'MedFlow Cơ sở 1 - Hà Nội',
      status: 'ACTIVE',
    };
  }
  if (idx === 9) {
    return {
      staffId: 219,
      code: 'STF-0219',
      fullName: 'Hoàng Quốc Bảo',
      email: 'bao.hoang@medflow.vn',
      roleCode: 'NURSE',
      role: 'Điều dưỡng',
      department: 'Khoa Nhi',
      facility: 'MedFlow Cơ sở 1 - Hà Nội',
      status: 'LOCKED',
    };
  }
  return {
    staffId: 300 + idx,
    code: `STF-030${idx}`,
    fullName: `Nhân sự ${idx}`,
    email: `staff${idx}@medflow.vn`,
    roleCode: 'NURSE',
    role: 'Điều dưỡng',
    department: 'Khoa Nội tổng hợp',
    facility: 'MedFlow Cơ sở 1 - Hà Nội',
    status: 'ACTIVE',
  };
});

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <StaffAccountListPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('StaffAccountListPage (SCR-ADM-STAFF-01 real API)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiClient.get).mockResolvedValue({
      data: {
        data: MOCK_STAFF_ITEMS,
        metrics: {
          total: 56,
          active: 48,
          locked: 5,
          disabled: 3,
        },
      },
    });
  });

  it('fetches GET /admin/staff and renders metric cards and table columns', async () => {
    renderPage();
    expect(await screen.findByText('STF-0142')).toBeInTheDocument();
    expect(screen.getByText('TÀI KHOẢN NHÂN SỰ')).toBeInTheDocument();
    expect(screen.getByText('56')).toBeInTheDocument();
    expect(screen.getByText('05')).toBeInTheDocument();
    expect(screen.getByText('minh.le@medflow.vn')).toBeInTheDocument();
  });

  it('filters by query, role, and status and paginates results', async () => {
    renderPage();
    expect(await screen.findByText('STF-0142')).toBeInTheDocument();

    expect(screen.getByText('Trang 1 / 2')).toBeInTheDocument();
    expect(screen.queryByText('STF-0219')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Trang sau' }));
    expect(screen.getByText('STF-0219')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Tìm kiếm tài khoản nhân sự'), {
      target: { value: 'STF-0089' },
    });
    expect(screen.getByText('Trần Hoài Nam')).toBeInTheDocument();
    expect(screen.queryByText('Lê Hoàng Minh')).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Tìm kiếm tài khoản nhân sự'), {
      target: { value: '' },
    });
    fireEvent.change(screen.getByLabelText('Lọc theo vai trò'), {
      target: { value: 'Nhân viên quầy' },
    });
    expect(screen.getByText('STF-0231')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Lọc theo trạng thái'), {
      target: { value: 'DISABLED' },
    });
    expect(screen.getByText('Không tìm thấy tài khoản nhân sự phù hợp.')).toBeInTheDocument();
  });
});
