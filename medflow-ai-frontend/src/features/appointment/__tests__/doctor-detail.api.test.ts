import { AxiosError } from 'axios';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { fetchDoctorDetailApi } from '../api/doctor-detail.api';
import type { DoctorDetailProfile } from '../types/doctor-detail.types';

describe('doctor-detail.api (M1-40 pure HTTP)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('calls GET /doctors/:idOrCode via apiClient and returns backend doctor detail profile', async () => {
    const backendPayload: DoctorDetailProfile = {
      id: '1',
      doctorCode: 'DOC-001',
      staffCode: 'STF-001',
      name: 'BS.CKI Nguyễn Minh Đức',
      title: 'BSCKI',
      roleTitle: 'Bác sĩ chuyên khoa I - Khoa Tiêu hóa',
      specialty: 'Khoa Tiêu hóa',
      specialtyCode: 'GI',
      departmentName: 'Khoa Tiêu hóa',
      room: 'Phòng 101 - Tầng 1, Khu Demo',
      statusText: 'Đang tiếp nhận lịch khám ngoại trú',
      price: 200000,
      avatarUrl: '/assets/admin-doctors/list-01.png',
      yearsOfExperience: 9,
      licenseNumber: 'DEMO-CCHN-001',
      education: [
        {
          id: 'edu-1-1',
          title: 'Bác sĩ Đa khoa',
          institution: 'Đại học Y Hà Nội (Hanoi Medical University)',
          badge: 'Tốt nghiệp Chính quy',
          type: 'degree',
        },
      ],
      biography: 'Bác sĩ khám ngoại trú chuyên khoa Tiêu hóa (dữ liệu demo).',
      hashtags: ['#NoiSoiTieuHoa', '#Gerd'],
      weeklySchedule: [
        {
          dayOfWeek: 'Thứ Ba',
          hasAvailableSlot: true,
          morningTime: '09:00 - 09:30',
          afternoonTime: '14:00 - 16:30',
          statusTag: 'Đang nhận bệnh',
          isActive: true,
        },
      ],
    };

    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: backendPayload });

    const result = await fetchDoctorDetailApi('DOC-001');

    expect(getSpy).toHaveBeenCalledWith('/doctors/DOC-001');
    expect(result.doctorCode).toBe('DOC-001');
    expect(result.name).toBe('BS.CKI Nguyễn Minh Đức');
    expect(result.room).toBe('Phòng 101 - Tầng 1, Khu Demo');
    expect(result.price).toBe(200000);
  });

  it('throws validation error immediately when doctorId is blank', async () => {
    const getSpy = vi.spyOn(apiClient, 'get');
    await expect(fetchDoctorDetailApi('   ')).rejects.toThrow('Mã bác sĩ không được để trống');
    expect(getSpy).not.toHaveBeenCalled();
  });

  it('rethrows HTTP 404, 400 and network errors without falling back to mock doctor profile', async () => {
    const notFoundError = new AxiosError(
      'Request failed with status code 404',
      'ERR_BAD_REQUEST',
      undefined,
      undefined,
      {
        status: 404,
        statusText: 'Not Found',
        headers: {},
        config: {} as never,
        data: { status: 404, title: 'Không tìm thấy', detail: 'Không tìm thấy Doctor.' },
      },
    );

    const getSpy = vi.spyOn(apiClient, 'get').mockRejectedValueOnce(notFoundError);
    await expect(fetchDoctorDetailApi('doc-01')).rejects.toBe(notFoundError);

    const badRequestError = new AxiosError(
      'Request failed with status code 400',
      'ERR_BAD_REQUEST',
      undefined,
      undefined,
      {
        status: 400,
        statusText: 'Bad Request',
        headers: {},
        config: {} as never,
        data: {
          status: 400,
          title: 'Dữ liệu không hợp lệ',
          detail: 'Mã bác sĩ chứa ký tự không hợp lệ.',
        },
      },
    );
    getSpy.mockRejectedValueOnce(badRequestError);
    await expect(fetchDoctorDetailApi('DOC-01@INVALID')).rejects.toBe(badRequestError);

    const networkError = new AxiosError('Network Error', 'ERR_NETWORK');
    getSpy.mockRejectedValueOnce(networkError);
    await expect(fetchDoctorDetailApi('DOC-001')).rejects.toBe(networkError);
  });
});
