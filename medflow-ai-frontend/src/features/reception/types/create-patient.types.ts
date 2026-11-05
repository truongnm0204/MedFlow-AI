export type PatientGender = 'MALE' | 'FEMALE' | 'OTHER';

export interface CreatePatientFormData {
  identityCard: string;
  fullName: string;
  dateOfBirth: string;
  gender: PatientGender;
  ethnicity: string;
  phone: string;
  email: string;
  address: string;
  insuranceNumber: string;
  insuranceRegisteredPlace: string;
  emergencyContactName: string;
  emergencyContactRelationship: string;
  emergencyContactPhone: string;
}

export interface CreatedPatientResult {
  /** Mã bệnh nhân phát sinh, ví dụ `BN-2024-0001`. */
  patientCode: string;
  fullName: string;
}
