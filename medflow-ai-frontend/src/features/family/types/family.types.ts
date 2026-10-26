export type RelationshipType =
  | 'Bản thân'
  | 'Con trai'
  | 'Con gái'
  | 'Vợ'
  | 'Chồng'
  | 'Mẹ ruột'
  | 'Bố ruột'
  | 'Khác';

export type RelationshipStatus = 'ACTIVE' | 'PENDING' | 'REVOKED' | 'NEEDS_REVIEW';

export interface FamilyMemberVitals {
  bloodType: string;
  heightCm: number;
  weightKg: number;
}

export interface FamilyMemberNotice {
  type: 'warning' | 'info';
  message: string;
}

export interface FamilyMemberPermissions {
  canManage: boolean;
  canBookAppointments: boolean;
  canViewMedicalRecords: boolean;
  relationshipStatus: RelationshipStatus;
  consentVerifiedAt?: string;
}

export interface FamilyMember {
  id: string;
  fullName: string;
  relationship: RelationshipType;
  relationshipBadgeColor?: 'slate' | 'blue' | 'rose' | 'amber';
  patientCode: string;
  subStatus: string;
  isPrimaryAccount: boolean;
  avatarUrl: string;
  birthDate: string;
  age: number;
  gender: 'Nam' | 'Nữ';
  nationality?: string;
  identificationNo?: string;
  identityIssueDate?: string;
  identityIssuePlace?: string;
  isIdentityVerified?: boolean;
  phone?: string;
  email?: string;
  address?: string;
  guardian?: string;
  emergencyContactName?: string;
  emergencyContactRelation?: string;
  emergencyPhone?: string;
  latestVisit?: string;
  followUpSchedule?: string;
  servicePackage?: string;
  clinicalNotes?: string;
  hasActiveAppointment?: boolean;
  activeAppointmentDetails?: string;
  outpatientVisitsCount?: number;
  activePrescriptionsCount?: number;
  vitals?: FamilyMemberVitals;
  notice?: FamilyMemberNotice;
  permissions?: FamilyMemberPermissions;
}
