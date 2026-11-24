import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import * as authApi from '@/features/auth/api/auth.api';
import { useAuthStore } from '@/features/auth/store/auth.store';

import * as recordsApi from '../api/patient-records.api';
import { PatientPrescriptionDetailPage } from '../pages/patient-prescription-detail-page';
import { PatientPrescriptionListPage } from '../pages/patient-prescription-list-page';

vi.mock('@/features/auth/api/auth.api', () => ({
  getMe: vi.fn(),
  logout: vi.fn(),
}));

vi.mock('../api/patient-records.api', () => ({
  getMyMedicalRecords: vi.fn(),
  getMyMedicalRecordDetail: vi.fn(),
  getMyPrescriptions: vi.fn(),
  getMyPrescriptionDetail: vi.fn(),
}));

function renderRxRoutes(initialEntry: string) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/patient/prescriptions" element={<PatientPrescriptionListPage />} />
          <Route
            path="/emr/prescriptions/:prescriptionCode"
            element={<PatientPrescriptionDetailPage />}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('PatientPrescriptionListPage & PatientPrescriptionDetailPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    const user = {
      id: 1,
      email: 'patient@medflow.vn',
      fullName: 'Nguyễn Văn An',
      roles: ['PATIENT'],
      patientCode: 'PAT-001',
    };
    useAuthStore.setState({
      user,
      accessToken: 'test-token',
      refreshToken: 'test-refresh',
      isAuthenticated: true,
      sessionEpoch: 1,
    });
    vi.mocked(authApi.getMe).mockResolvedValue(user);
  });

  it('renders prescriptions list, filters by status, and links to detail and EMR', async () => {
    vi.mocked(recordsApi.getMyPrescriptions).mockResolvedValue({
      items: [
        {
          id: 101,
          prescriptionCode: 'RX-2026-0101',
          visitId: 301,
          visitCode: 'VIS-2026-0301',
          status: 'DISPENSED',
          issuedAt: '2026-11-15T10:30:00Z',
          prescribingDoctorName: 'BS.CKI Trần Quốc Tuấn',
          hospitalName: 'Bệnh viện MedFlow',
          itemCount: 2,
        },
        {
          id: 102,
          prescriptionCode: 'RX-2026-0102',
          visitId: 302,
          visitCode: 'VIS-2026-0302',
          status: 'ISSUED',
          issuedAt: '2026-11-16T08:00:00Z',
          prescribingDoctorName: 'ThS.BS Lê Nam',
          hospitalName: 'Bệnh viện MedFlow',
          itemCount: 1,
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 2,
    });

    renderRxRoutes('/patient/prescriptions');

    expect(await screen.findByText('RX-2026-0101')).toBeInTheDocument();
    expect(screen.getByText('RX-2026-0102')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Trạng thái cấp phát'), {
      target: { value: 'DISPENSED' },
    });

    expect(screen.getByText('RX-2026-0101')).toBeInTheDocument();
    expect(screen.queryByText('RX-2026-0102')).not.toBeInTheDocument();

    expect(screen.getByRole('link', { name: /Xem hướng dẫn dùng thuốc/i })).toHaveAttribute(
      'href',
      '/emr/prescriptions/RX-2026-0101',
    );
    expect(screen.getByRole('link', { name: /Xem bệnh án lượt khám/i })).toHaveAttribute(
      'href',
      '/emr/VIS-2026-0301',
    );
  });

  it('renders prescription detail with medicines, dosages, and safety instructions', async () => {
    vi.mocked(recordsApi.getMyPrescriptionDetail).mockResolvedValue({
      prescription: {
        id: 101,
        prescriptionCode: 'RX-2026-0101',
        visitId: 301,
        visitCode: 'VIS-2026-0301',
        status: 'READY',
        issuedAt: '2026-11-15T10:30:00Z',
        prescribingDoctorName: 'BS.CKI Trần Quốc Tuấn',
        hospitalName: 'Bệnh viện MedFlow',
        itemCount: 1,
      },
      items: [
        {
          id: 1,
          medicineCode: 'MED-PARA',
          medicineName: 'Paracetamol',
          activeIngredient: 'Acetaminophen',
          strength: '500mg',
          dosageForm: 'Viên nén',
          prescribedQuantity: 15,
          dispensingUnit: 'Viên',
          singleDoseQuantity: 1,
          singleDoseUnit: 'Viên',
          doseScheduleText: 'Sáng 1 viên, Trưa 1 viên, Tối 1 viên',
          frequency: '3 lần/ngày',
          durationDays: 5,
          administrationRoute: 'Uống',
          instructions: 'Uống sau ăn no khi sốt trên 38.5°C',
        },
      ],
    });

    renderRxRoutes('/emr/prescriptions/RX-2026-0101');

    expect(await screen.findByText('Chi tiết đơn thuốc & Hướng dẫn sử dụng')).toBeInTheDocument();
    expect(screen.getByText(/Paracetamol 500mg/)).toBeInTheDocument();
    expect(screen.getByText('Uống sau ăn no khi sốt trên 38.5°C')).toBeInTheDocument();
    expect(screen.getByText('Trong 5 ngày')).toBeInTheDocument();
    expect(screen.getByText('Sẵn sàng nhận thuốc')).toBeInTheDocument();
  });
});
