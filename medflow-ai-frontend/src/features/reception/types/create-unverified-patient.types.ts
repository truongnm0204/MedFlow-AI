import type { PatientUnverifiedReason } from '@/features/ekyc/types/ekyc.types';

import type { PatientGender } from './create-patient.types';

export interface CreateUnverifiedPatientFormData {
  fullName: string;
  dateOfBirth: string;
  /** Bỏ trống khi chưa xác định giới tính. */
  gender: PatientGender | '';
  phone: string;
  email: string;
  address: string;
  identityCard: string;
  /** Bắt buộc — một trong 6 lý do owner-approved. */
  reason: PatientUnverifiedReason | '';
  note: string;
}
