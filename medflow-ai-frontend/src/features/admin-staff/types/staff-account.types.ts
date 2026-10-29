export type StaffRole = 'Bác sĩ' | 'Kỹ thuật viên' | 'Điều dưỡng' | 'Nhân viên quầy';
export type StaffAccountStatus = 'ACTIVE' | 'LOCKED' | 'DISABLED';

export interface StaffAccountItem {
  id: string;
  code: string;
  fullName: string;
  email: string;
  role: StaffRole;
  department: string;
  facility: string;
  status: StaffAccountStatus;
}

export interface StaffAccountFilters {
  query: string;
  department: string;
  role: string;
  status: string;
}

export interface StaffAccountMetrics {
  total: number;
  active: number;
  locked: number;
  disabled: number;
}
