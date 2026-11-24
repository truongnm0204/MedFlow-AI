import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import {
  getMyMedicalRecordDetail,
  getMyMedicalRecords,
  getMyPrescriptionDetail,
  getMyPrescriptions,
} from '../api/patient-records.api';
import { medicalRecordFilterSchema } from '../schemas/medical-record-filter.schema';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
  },
}));

describe('patient-records.api & filter schema', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('calls GET /patients/me/medical-records with sanitized params', async () => {
    vi.mocked(apiClient.get).mockResolvedValueOnce({
      data: {
        items: [],
        page: 1,
        pageSize: 10,
        totalCount: 0,
        members: [],
        hospitals: [],
        specialties: [],
      },
    });

    await getMyMedicalRecords({
      patientId: 2,
      fromDate: ' 2026-11-01 ',
      toDate: '2026-11-20',
      hospitalId: 10,
      specialtyCode: 'CARDIO',
      keyword: '  K29.0  ',
      page: 1,
      pageSize: 10,
    });

    expect(apiClient.get).toHaveBeenCalledWith('/patients/me/medical-records', {
      params: {
        patientId: 2,
        fromDate: '2026-11-01',
        toDate: '2026-11-20',
        hospitalId: 10,
        specialtyCode: 'CARDIO',
        keyword: 'K29.0',
        page: 1,
        pageSize: 10,
      },
    });
  });

  it('calls GET /patients/me/medical-records/:idOrCode with encoded identifier', async () => {
    vi.mocked(apiClient.get).mockResolvedValueOnce({
      data: { medicalRecordId: 501, recordCode: 'EMR-501' },
    });

    await getMyMedicalRecordDetail(' EMR-501 ');

    expect(apiClient.get).toHaveBeenCalledWith('/patients/me/medical-records/EMR-501');
  });

  it('calls GET /patients/me/prescriptions and /patients/me/prescriptions/:code', async () => {
    vi.mocked(apiClient.get)
      .mockResolvedValueOnce({
        data: { items: [], page: 2, pageSize: 20, totalCount: 0 },
      })
      .mockResolvedValueOnce({
        data: { prescription: { id: 101, prescriptionCode: 'RX-101' }, items: [] },
      });

    await getMyPrescriptions({ page: 2, pageSize: 20 });
    await getMyPrescriptionDetail('RX-101');

    expect(apiClient.get).toHaveBeenNthCalledWith(1, '/patients/me/prescriptions', {
      params: { page: 2, pageSize: 20 },
    });
    expect(apiClient.get).toHaveBeenNthCalledWith(2, '/patients/me/prescriptions/RX-101');
  });

  it('validates date range and blocks angle brackets in medicalRecordFilterSchema', () => {
    const invalidRange = medicalRecordFilterSchema.safeParse({
      patientId: '',
      fromDate: '2026-11-25',
      toDate: '2026-11-10',
      hospitalId: '',
      specialtyCode: '',
      keyword: '',
    });
    expect(invalidRange.success).toBe(false);

    const invalidKeyword = medicalRecordFilterSchema.safeParse({
      patientId: '',
      fromDate: '',
      toDate: '',
      hospitalId: '',
      specialtyCode: '',
      keyword: '<script>',
    });
    expect(invalidKeyword.success).toBe(false);

    const valid = medicalRecordFilterSchema.safeParse({
      patientId: '1',
      fromDate: '2026-11-01',
      toDate: '2026-11-25',
      hospitalId: '10',
      specialtyCode: 'CARDIO',
      keyword: 'Viêm dạ dày',
    });
    expect(valid.success).toBe(true);
  });
});
