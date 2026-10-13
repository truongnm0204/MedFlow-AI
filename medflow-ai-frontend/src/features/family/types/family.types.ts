export type RelationshipType =
  'Bản thân' | 'Con trai' | 'Con gái' | 'Vợ' | 'Chồng' | 'Mẹ ruột' | 'Bố ruột' | 'Khác';

export interface FamilyMemberVitals {
  bloodType: string;
  heightCm: number;
  weightKg: number;
}

export interface FamilyMemberNotice {
  type: 'warning' | 'info';
  message: string;
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
  phone?: string;
  email?: string;
  guardian?: string;
  emergencyPhone?: string;
  latestVisit?: string;
  followUpSchedule?: string;
  servicePackage?: string;
  clinicalNotes?: string;
  vitals?: FamilyMemberVitals;
  notice?: FamilyMemberNotice;
}
