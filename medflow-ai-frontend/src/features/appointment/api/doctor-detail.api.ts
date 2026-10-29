import { getDoctorDetailById } from '../data/doctor-detail-mock';
import type { DoctorDetailProfile } from '../types/doctor-detail.types';

export async function fetchDoctorDetailApi(doctorId: string): Promise<DoctorDetailProfile> {
  const profile = getDoctorDetailById(doctorId);
  if (!profile) {
    throw new Error('Không tìm thấy thông tin hồ sơ bác sĩ');
  }
  return profile;
}
