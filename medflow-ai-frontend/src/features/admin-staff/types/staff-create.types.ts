export type StaffAuthProvider = 'LOCAL' | 'GOOGLE';
export type StaffAccountStatus = 'ACTIVE' | 'LOCKED';
export type StaffRole = 'DOCTOR' | 'TECHNICIAN' | 'NURSE' | 'RECEPTIONIST';

export interface StaffRoleOption {
  value: StaffRole;
  label: string;
}

export interface CreateStaffAccountInput {
  staffCode: string;
  fullName: string;
  authProvider: StaffAuthProvider;
  facility: string;
  email: string;
  password: string;
  status: StaffAccountStatus;
  role: StaffRole;
  department: string;
}
