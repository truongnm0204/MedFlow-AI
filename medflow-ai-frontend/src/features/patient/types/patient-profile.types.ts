export type GenderType = 'Nam' | 'Nữ' | 'Khác';

export interface PatientIdentity {
  fullName: string;
  dateOfBirth: string;
  age: number;
  gender: GenderType;
  nationality: string;
  identityNumber: string;
  identityIssueDate: string;
  identityIssuePlace: string;
  isIdentityVerified: boolean;
  pidNumber?: string;
}

export interface PatientVitals {
  bloodType: string;
  heightCm: number;
  weightKg: number;
}

export interface PatientShortcuts {
  familyMembersCount: number;
  outpatientVisitsCount: number;
  activePrescriptionsCount: number;
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
  avatarUrl?: string;
  identity: PatientIdentity;
  vitals: PatientVitals;
  shortcuts: PatientShortcuts;
  contact: PatientContact;
}
