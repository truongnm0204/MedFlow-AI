import { apiClient } from '@/shared/lib/axios';

import type { DoctorDetailProfile } from '../types/doctor-detail.types';

export async function fetchDoctorDetailApi(doctorId: string): Promise<DoctorDetailProfile> {
  const normalized = doctorId.trim();
  if (!normalized) {
    throw new Error('Mã bác sĩ không được để trống');
  }

  const { data } = await apiClient.get<DoctorDetailProfile>(
    `/doctors/${encodeURIComponent(normalized)}`,
  );
  return data;
}
