export type GenderType = 'Nam' | 'Nữ' | 'Khác' | 'Chưa cập nhật';

export type GenderCode = 'MALE' | 'FEMALE' | 'OTHER';

export interface PatientIdentity {
  fullName: string;
  dateOfBirth: string;
  rawDateOfBirthIso?: string | null;
  age: number | null;
  gender: GenderType;
  rawGenderCode?: GenderCode | null;
  nationality: string;
  identityNumber: string;
  identityIssueDate: string;
  identityIssuePlace: string;
  isIdentityVerified: boolean;
  pidNumber?: string;
}

export interface PatientVitals {
  bloodType: string;
  heightCm: number | null;
  weightKg: number | null;
}

export interface PatientShortcuts {
  familyMembersCount: number | null;
  outpatientVisitsCount: number | null;
  activePrescriptionsCount: number | null;
}

export interface EmergencyContact {
  fullName: string;
  relationship: string;
  phone: string;
}

export interface PatientContact {
  phone: string;
  isPhoneVerified: boolean;
  email: string;
  isEmailVerified: boolean;
  permanentAddress: string;
  emergencyContact: EmergencyContact;
}

export interface PatientProfile {
  id: string;
  avatarUrl?: string | null;
  identity: PatientIdentity;
  vitals: PatientVitals;
  shortcuts: PatientShortcuts;
  contact: PatientContact;
}

export interface UpdateEmergencyContactInput {
  fullName: string;
  relationship: string;
  phone: string;
}

export interface UpdateMyPatientProfileInput {
  phone: string;
  address: string | null;
  bloodType: string | null;
  dateOfBirth?: string | null;
  gender?: GenderCode | null;
  emergencyContact: UpdateEmergencyContactInput | null;
}
