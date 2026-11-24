import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import * as authApi from '@/features/auth/api/auth.api';
import { useAuthStore } from '@/features/auth/store/auth.store';

import * as recordsApi from '../api/patient-records.api';
import { MedicalRecordListPage } from '../pages/medical-record-list-page';
import type { PatientMedicalRecordListResponse } from '../types/patient-records.types';

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

function renderListPage(initialEntry = '/emr') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/emr" element={<MedicalRecordListPage />} />
          <Route path="/patient/emr" element={<MedicalRecordListPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const sampleResponse: PatientMedicalRecordListResponse = {
  items: [
    {
      medicalRecordId: 501,
      recordCode: 'EMR-501',
      visitId: 301,
      visitCode: 'VIS-2026-0301',
      visitDate: '2026-11-15',
      checkedInAt: '2026-11-15T09:30:00Z',
      completedAt: '2026-11-15T10:30:00Z',
      finalizedAt: '2026-11-15T10:30:00Z',
      patientId: 1,
      patientCode: 'PAT-001',
      patientName: 'Nguyễn Văn An',
      relationshipLabel: 'Bản thân',
      isSelfProfile: true,
      hospitalId: 10,
      hospitalName: 'Bệnh viện Đa khoa MedFlow',
      departmentName: 'Khoa Nội',
      specialtyId: 20,
      specialtyCode: 'INT-MED',
      specialtyName: 'Nội tổng quát',
      doctorName: 'BS.CKI Trần Quốc Tuấn',
      primaryIcd10Code: 'K29.0',
      primaryDiagnosisName: 'Viêm dạ dày cấp',
      clinicalConclusion: 'Uống thuốc đủ 14 ngày, tái khám khi đau tăng.',
      diagnosisCount: 1,
      diagnosticOrderCount: 2,
      prescriptionCount: 1,
    },
  ],
  page: 1,
  pageSize: 10,
  totalCount: 1,
  members: [
    {
      patientId: 1,
      patientCode: 'PAT-001',
      fullName: 'Nguyễn Văn An',
      relationshipLabel: 'Bản thân',
      isSelf: true,
    },
    {
      patientId: 2,
      patientCode: 'PAT-002',
      fullName: 'Bé Nguyễn Minh',
      relationshipLabel: 'Con trai',
      isSelf: false,
    },
  ],
  hospitals: [{ id: 10, code: 'BV-01', name: 'Bệnh viện Đa khoa MedFlow' }],
  specialties: [{ id: 20, code: 'INT-MED', name: 'Nội tổng quát' }],
};

describe('MedicalRecordListPage', () => {
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

  it('renders medical records list and links to detail page', async () => {
    vi.mocked(recordsApi.getMyMedicalRecords).mockResolvedValue(sampleResponse);

    renderListPage('/emr');

    expect(await screen.findByText('EMR-501')).toBeInTheDocument();
    expect(screen.getByText('VIS-2026-0301')).toBeInTheDocument();
    expect(screen.getByText('K29.0')).toBeInTheDocument();
    expect(screen.getByText(/BS\.CKI Trần Quốc Tuấn/)).toBeInTheDocument();

    const detailLink = screen.getByRole('link', { name: /Xem chi tiết bệnh án/i });
    expect(detailLink).toHaveAttribute('href', '/emr/EMR-501');
  });

  it('validates date range on filter submit and applies valid filters', async () => {
    vi.mocked(recordsApi.getMyMedicalRecords).mockResolvedValue(sampleResponse);

    renderListPage('/patient/emr');
    await screen.findByText('EMR-501');

    fireEvent.change(screen.getByLabelText('Từ ngày khám'), {
      target: { value: '2026-11-25' },
    });
    fireEvent.change(screen.getByLabelText('Đến ngày khám'), {
      target: { value: '2026-11-10' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Áp dụng bộ lọc/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Ngày bắt đầu không được lớn hơn ngày kết thúc.',
    );
    expect(recordsApi.getMyMedicalRecords).toHaveBeenCalledTimes(1);

    fireEvent.change(screen.getByLabelText('Đến ngày khám'), {
      target: { value: '2026-11-30' },
    });
    fireEvent.change(screen.getByLabelText('Thành viên gia đình'), {
      target: { value: '2' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Áp dụng bộ lọc/i }));

    await waitFor(() => {
      expect(recordsApi.getMyMedicalRecords).toHaveBeenLastCalledWith(
        expect.objectContaining({
          patientId: 2,
          fromDate: '2026-11-25',
          toDate: '2026-11-30',
          page: 1,
        }),
      );
    });
  });

  it('renders empty state when no completed records match', async () => {
    vi.mocked(recordsApi.getMyMedicalRecords).mockResolvedValue({
      ...sampleResponse,
      items: [],
      totalCount: 0,
    });

    renderListPage('/emr');

    expect(await screen.findByText('Chưa có hồ sơ bệnh án hoàn tất phù hợp')).toBeInTheDocument();
  });
});
