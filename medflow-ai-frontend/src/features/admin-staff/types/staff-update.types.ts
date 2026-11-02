export type StaffAuthMethod = 'LOCAL' | 'GOOGLE';
export type StaffAccountStatus = 'ACTIVE' | 'LOCKED' | 'DISABLED';
export type StaffRole = 'DOCTOR' | 'TECHNICIAN' | 'NURSE' | 'RECEPTIONIST';

export interface UpdateStaffAccountInput {
  fullName: string;
  authMethod: StaffAuthMethod;
  facility: string;
  email: string;
  status: StaffAccountStatus;
  role: StaffRole;
  department: string;
}

export interface StaffAccountRecord extends UpdateStaffAccountInput {
  staffCode: string;
}
