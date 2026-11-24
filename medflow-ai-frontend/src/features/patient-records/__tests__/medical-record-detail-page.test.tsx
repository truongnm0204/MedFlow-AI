import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import * as authApi from '@/features/auth/api/auth.api';
import { useAuthStore } from '@/features/auth/store/auth.store';

import * as recordsApi from '../api/patient-records.api';
import { MedicalRecordDetailPage } from '../pages/medical-record-detail-page';
import type { PatientMedicalRecordDetail } from '../types/patient-records.types';

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

function renderDetailPage(entry = '/emr/EMR-501') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path="/emr/:recordId" element={<MedicalRecordDetailPage />} />
          <Route path="/patient/emr/:recordId" element={<MedicalRecordDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const sampleDetail: PatientMedicalRecordDetail = {
  medicalRecordId: 501,
  recordCode: 'EMR-501',
  status: 'FINALIZED',
  finalizedAt: '2026-11-15T10:30:00Z',
  visitId: 301,
  visitCode: 'VIS-2026-0301',
  visitType: 'SCHEDULED',
  visitDate: '2026-11-15',
  checkedInAt: '2026-11-15T09:15:00Z',
  completedAt: '2026-11-15T10:30:00Z',
  reasonForVisit: 'Đau thượng vị',
  patientId: 1,
  patientCode: 'PAT-001',
  patientName: 'Nguyễn Văn An',
  relationshipLabel: 'Bản thân',
  isSelfProfile: true,
  gender: 'MALE',
  dateOfBirth: '1992-05-14',
  bloodType: 'O+',
  hospitalId: 10,
  hospitalCode: 'BV-01',
  hospitalName: 'Bệnh viện Đa khoa MedFlow',
  departmentName: 'Khoa Nội',
  specialtyId: 20,
  specialtyCode: 'INT-MED',
  specialtyName: 'Nội tổng quát',
  roomCode: 'P101',
  roomName: 'Phòng Nội 101',
  doctorName: 'BS.CKI Trần Quốc Tuấn',
  presentingComplaint: 'Đau thượng vị âm ỉ 3 ngày',
  symptomHistory: 'Đau tăng sau ăn chua cay',
  relevantHistoryAllergies: 'Không dị ứng thuốc',
  clinicalFindings: 'Bụng mềm, ấn tức thượng vị',
  clinicalConclusion: 'Viêm dạ dày cấp ổn định, tái khám sau 2 tuần.',
  recordedAt: '2026-11-15T10:25:00Z',
  vitalSigns: {
    bpSystolicMmhg: 120,
    bpDiastolicMmhg: 80,
    bloodPressureLabel: '120/80 mmHg',
    pulseBpm: 74,
    temperatureC: 36.7,
    spo2Percent: 98,
    heightCm: 175,
    weightKg: 70,
    bmi: 22.9,
  },
  diagnoses: [
    {
      diagnosisId: 1,
      icd10Code: 'K29.0',
      icd10NameVi: 'Viêm dạ dày cấp',
      clinicalDiagnosis: 'Viêm niêm mạc hang vị cấp',
      isPrimary: true,
      confirmedAt: '2026-11-15T10:30:00Z',
    },
  ],
  diagnosticOrders: [
    {
      orderId: 91,
      orderCode: 'ORD-2026-0142',
      serviceCode: 'CLS-ENDO',
      serviceName: 'Nội soi dạ dày',
      priority: 'NORMAL',
      status: 'PUBLISHED',
      orderedAt: '2026-11-15T09:40:00Z',
      isResultPublished: true,
      signedAt: '2026-11-15T10:10:00Z',
      conclusionSummary: 'Viêm trợt hang vị nhẹ.',
      hasReportFile: true,
    },
  ],
  prescriptions: [
    {
      prescriptionId: 201,
      prescriptionCode: 'RX-2026-0201',
      status: 'ISSUED',
      issuedAt: '2026-11-15T10:30:00Z',
      prescribingDoctorName: 'BS.CKI Trần Quốc Tuấn',
      itemCount: 1,
      canViewPrescriptionDetail: true,
      items: [
        {
          itemId: 1,
          medicineCode: 'MED-OME',
          medicineName: 'Omeprazol',
          strength: '20mg',
          dosageForm: 'Viên nang',
          prescribedQuantity: 14,
          dispensingUnit: 'Viên',
          doseScheduleText: 'Sáng 1 viên, Tối 1 viên',
          frequency: '2 lần/ngày',
          durationDays: 7,
          administrationRoute: 'Uống',
          instructions: 'Uống trước ăn 30 phút',
        },
      ],
    },
  ],
};

describe('MedicalRecordDetailPage', () => {
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

  it('renders vitals, ICD-10 diagnoses, conclusion, CLS link and prescription link', async () => {
    vi.mocked(recordsApi.getMyMedicalRecordDetail).mockResolvedValue(sampleDetail);

    renderDetailPage('/emr/EMR-501');

    expect(
      await screen.findByText(/Hồ sơ khám bệnh ngoại trú — Nguyễn Văn An/),
    ).toBeInTheDocument();
    expect(screen.getByText('120/80 mmHg')).toBeInTheDocument();
    expect(screen.getByText('22.9')).toBeInTheDocument();
    expect(screen.getByText('K29.0')).toBeInTheDocument();
    expect(screen.getByText('Viêm dạ dày cấp ổn định, tái khám sau 2 tuần.')).toBeInTheDocument();

    const clsLink = screen.getByRole('link', { name: /Xem kết quả CLS/i });
    expect(clsLink).toHaveAttribute('href', '/emr/diagnostic-result/ORD-2026-0142');

    const rxLink = screen.getByRole('link', { name: /Xem chi tiết đơn thuốc/i });
    expect(rxLink).toHaveAttribute('href', '/emr/prescriptions/RX-2026-0201');
  });

  it('hides standalone prescription detail link for family member record while keeping inline medicines', async () => {
    vi.mocked(recordsApi.getMyMedicalRecordDetail).mockResolvedValue({
      ...sampleDetail,
      isSelfProfile: false,
      relationshipLabel: 'Con trai',
      prescriptions: sampleDetail.prescriptions.map((rx) => ({
        ...rx,
        canViewPrescriptionDetail: false,
      })),
    });

    renderDetailPage('/patient/emr/EMR-501');

    expect(await screen.findByText(/Omeprazol/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Xem chi tiết đơn thuốc/i })).not.toBeInTheDocument();
  });

  it('renders error alert when record access is forbidden or missing', async () => {
    vi.mocked(recordsApi.getMyMedicalRecordDetail).mockRejectedValue(
      new Error('Bạn không có quyền xem hồ sơ bệnh án này.'),
    );

    renderDetailPage('/emr/EMR-999');

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Không tìm thấy hoặc không có quyền truy cập bệnh án',
    );
  });
});
