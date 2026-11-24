import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { DoctorEditModal } from '../pages/doctor-edit-modal';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    put: vi.fn(),
  },
}));

const MOCK_API_DOCTORS = [
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
    profileImageUrl: '/assets/admin-doctors/list-02.png',
    professionalBio: 'Bác sĩ chuyên khoa tiêu hóa',
    qualificationsSummary: 'Khám Nội\n---\n2015: ĐHY Hà Nội',
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
];

describe('DoctorEditModal (SCR-ADM-DOC-03 real API)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiClient.get).mockResolvedValue({
      data: { data: MOCK_API_DOCTORS },
    });
    vi.mocked(apiClient.put).mockImplementation(async (_url, body) => ({
      data: {
        ...MOCK_API_DOCTORS[0],
        ...(body as Record<string, unknown>),
      },
    }));
  });

  const renderModal = (id = 'DOC-0142') => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={[`/admin/doctors/${id}/edit`]}>
          <Routes>
            <Route path="/admin/doctors/:doctorId/edit" element={<DoctorEditModal />} />
            <Route path="/admin/doctors" element={<div>Danh sách Bác sĩ</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );
  };

  it('prefills doctor profile from GET /admin/doctors and shows not-found for unknown ID', async () => {
    const first = renderModal('DOC-0142');
    expect(
      await screen.findByRole('heading', { name: 'Hồ sơ Bác sĩ - BS. CKI Lê Hoàng Minh' }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText(/Mã Bác sĩ \(Practitioner ID\)/)).toHaveValue('DOC-0142');
    expect(screen.getByLabelText(/Mã Bác sĩ \(Practitioner ID\)/)).toHaveAttribute('readonly');
    first.unmount();

    renderModal('DOC-9999');
    expect(await screen.findByText('Không tìm thấy hồ sơ bác sĩ.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Quay lại danh sách' }));
    expect(screen.getByText('Danh sách Bác sĩ')).toBeInTheDocument();
  });

  it('validates invalid fields and submits PUT /admin/doctors/:id on valid save', async () => {
    renderModal('DOC-0142');
    expect(await screen.findByDisplayValue('BS. CKI Lê Hoàng Minh')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/Họ và tên Bác sĩ/), { target: { value: 'A' } });
    fireEvent.click(screen.getByRole('button', { name: /Lưu hồ sơ Bác sĩ/ }));
    expect(await screen.findByText('Vui lòng nhập họ tên')).toBeInTheDocument();
    expect(apiClient.put).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText(/Họ và tên Bác sĩ/), {
      target: { value: 'BS. CKI Lê Hoàng Minh (Cập nhật)' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Lưu hồ sơ Bác sĩ/ }));

    await waitFor(() => {
      expect(apiClient.put).toHaveBeenCalledWith(
        '/admin/doctors/142',
        expect.objectContaining({
          name: 'BS. CKI Lê Hoàng Minh (Cập nhật)',
          isActive: true,
        }),
      );
      expect(screen.getByText('Danh sách Bác sĩ')).toBeInTheDocument();
    });
  });

  it('guards deactivation when pendingAppointmentsCount > 0 and allows when 0', async () => {
    const first = renderModal('DOC-0142');
    expect(await screen.findByText('Có 6 ca hẹn tuần này')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('checkbox', { name: /Trạng thái hoạt động bác sĩ/ }));
    expect(
      screen.getByText(/Không thể ngừng hoạt động khi còn 6 lượt khám đang xử lý/i),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ngừng hoạt động' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Giữ hồ sơ' }));
    first.unmount();

    renderModal('DOC-0089');
    expect(await screen.findByText('Không có ca hẹn tuần này')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('checkbox', { name: /Trạng thái hoạt động bác sĩ/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Ngừng hoạt động' }));
    expect(screen.getByText('○ Ngừng hoạt động (Inactive)')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Lưu hồ sơ Bác sĩ/ }));
    await waitFor(() => {
      expect(apiClient.put).toHaveBeenCalledWith(
        '/admin/doctors/89',
        expect.objectContaining({ isActive: false }),
      );
    });
  });
});
