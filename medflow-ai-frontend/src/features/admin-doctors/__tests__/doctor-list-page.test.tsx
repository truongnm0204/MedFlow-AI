import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { DoctorListPage } from '../pages/doctor-list-page';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    patch: vi.fn(),
  },
}));

const INITIAL_API_DOCTORS = [
  {
    id: 142,
    doctorCode: 'DOC-0142',
    staffCode: 'NV-1142',
    fullName: 'BS. CKI Lê Hoàng Minh',
    email: 'minh.le@medflow.vn',
    degree: 'Bác sĩ Chuyên khoa I',
    specialtyName: 'Nội Tiêu hóa',
    departmentName: 'Khoa Nội Tiêu hóa',
    licenseNumber: '014200/BYT-CCHN',
    yearsOfExperience: 10,
    isActive: true,
    hasPendingAppointments: true,
    pendingAppointmentsCount: 6,
  },
  {
    id: 89,
    doctorCode: 'DOC-0089',
    staffCode: 'NV-1089',
    fullName: 'PGS. TS. BS Trần Hoài Nam',
    email: 'nam.tran@medflow.vn',
    degree: 'Phó Giáo sư - Tiến sĩ',
    specialtyName: 'Tim mạch can thiệp',
    departmentName: 'Khoa Tim mạch',
    licenseNumber: '008900/BYT-CCHN',
    yearsOfExperience: 18,
    isActive: true,
    hasPendingAppointments: false,
    pendingAppointmentsCount: 0,
  },
  {
    id: 210,
    doctorCode: 'DOC-0210',
    staffCode: 'NV-1210',
    fullName: 'ThS. BS Nguyễn Thu Hà',
    email: 'ha.nguyen@medflow.vn',
    degree: 'Thạc sĩ Bác sĩ',
    specialtyName: 'Nhi tổng quát',
    departmentName: 'Khoa Nhi',
    licenseNumber: '021000/BYT-CCHN',
    yearsOfExperience: 7,
    isActive: false,
    hasPendingAppointments: false,
    pendingAppointmentsCount: 0,
  },
];

describe('DoctorListPage (SCR-ADM-DOC-01 & SCR-ADM-DOC-05 real API)', () => {
  let doctorsDb: typeof INITIAL_API_DOCTORS;

  beforeEach(() => {
    vi.clearAllMocks();
    doctorsDb = JSON.parse(JSON.stringify(INITIAL_API_DOCTORS));

    vi.mocked(apiClient.get).mockImplementation(async () => ({
      data: { data: doctorsDb, total: doctorsDb.length },
    }));

    vi.mocked(apiClient.patch).mockImplementation(async (url) => {
      const parts = String(url).split('/');
      const action = parts.pop();
      const code = parts.pop();
      const idx = doctorsDb.findIndex((d) => d.doctorCode === code);
      if (idx >= 0) {
        doctorsDb[idx] = { ...doctorsDb[idx], isActive: action === 'reactivate' };
      }
      return { data: {} };
    });
  });

  const renderPage = () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={qc}>
        <MemoryRouter>
          <DoctorListPage />
        </MemoryRouter>
      </QueryClientProvider>,
    );
  };

  it('fetches GET /admin/doctors and renders metric cards and status filters', async () => {
    renderPage();
    expect(await screen.findByText('DOC-0142')).toBeInTheDocument();

    expect(screen.getByText('TỔNG SỐ BÁC SĨ CƠ SỞ')).toBeInTheDocument();
    expect(screen.getByText('ĐANG HOẠT ĐỘNG')).toBeInTheDocument();
    expect(screen.getByText('NGỪNG HOẠT ĐỘNG')).toBeInTheDocument();
    expect(screen.getByText('TRÌNH ĐỘ SAU ĐẠI HỌC')).toBeInTheDocument();

    const statusSelect = screen.getByLabelText('Lọc trạng thái');
    fireEvent.change(statusSelect, { target: { value: 'false' } });
    expect(screen.getByText('DOC-0210')).toBeInTheDocument();
    expect(screen.queryByText('DOC-0142')).not.toBeInTheDocument();
  });

  it('calls PATCH /admin/doctors/:code/deactivate when doctor has 0 pending appointments and blocks when pending > 0', async () => {
    renderPage();
    expect(await screen.findByText('DOC-0089')).toBeInTheDocument();

    // Blocked doctor DOC-0142 (6 pending)
    fireEvent.click(screen.getByRole('button', { name: 'Thao tác BS. CKI Lê Hoàng Minh' }));
    fireEvent.click(screen.getByRole('button', { name: 'Ngừng hoạt động BS. CKI Lê Hoàng Minh' }));
    const blockedDialog = screen.getByRole('dialog');
    expect(
      within(blockedDialog).getByText(/Không thể ngừng hoạt động khi còn 6 lượt khám đang xử lý/i),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ngừng hoạt động' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Giữ hồ sơ' }));

    // Unblocked doctor DOC-0089 (0 pending)
    fireEvent.click(screen.getByRole('button', { name: 'Thao tác PGS. TS. BS Trần Hoài Nam' }));
    fireEvent.click(
      screen.getByRole('button', { name: 'Ngừng hoạt động PGS. TS. BS Trần Hoài Nam' }),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Ngừng hoạt động' }));

    await waitFor(() => {
      expect(apiClient.patch).toHaveBeenCalledWith(
        '/admin/doctors/DOC-0089/deactivate',
        expect.any(Object),
      );
    });
    expect(
      await screen.findByText(
        /Đã chuyển trạng thái bác sĩ PGS\. TS\. BS Trần Hoài Nam sang Ngừng hoạt động\./i,
      ),
    ).toBeInTheDocument();
  });
});
