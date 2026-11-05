import type {
  StaffAccountRecord,
  StaffAccountStatus,
  StaffRole,
} from '../types/staff-update.types';

export const STAFF_FACILITIES = ['MedFlow Cơ sở 1 - Hà Nội', 'MedFlow Cơ sở 2 - TP.HCM'] as const;

export const STAFF_DEPARTMENTS = [
  'Khoa Nội Tiêu hóa',
  'Khoa Tim mạch',
  'Khoa Khám bệnh',
  'Khoa Xét nghiệm',
  'Khoa Chẩn đoán hình ảnh',
] as const;

export const STAFF_ROLE_LABELS: Record<StaffRole, string> = {
  DOCTOR: 'Bác sĩ',
  TECHNICIAN: 'Kỹ thuật viên',
  NURSE: 'Điều dưỡng',
  RECEPTIONIST: 'Nhân viên quầy',
};

export const STAFF_STATUS_LABELS: Record<StaffAccountStatus, string> = {
  ACTIVE: 'Đang hoạt động',
  LOCKED: 'Đã khóa',
  DISABLED: 'Đã vô hiệu hóa',
};

export const STAFF_DEMO_RECORDS: StaffAccountRecord[] = [
  {
    staffCode: 'STF-0142',
    fullName: 'Lê Hoàng Minh',
    authMethod: 'LOCAL',
    facility: 'MedFlow Cơ sở 1 - Hà Nội',
    email: 'minh.le@medflow.vn',
    status: 'ACTIVE',
    role: 'DOCTOR',
    department: 'Khoa Nội Tiêu hóa',
  },
  {
    staffCode: 'STF-0143',
    fullName: 'Nguyễn Thu Hà',
    authMethod: 'GOOGLE',
    facility: 'MedFlow Cơ sở 2 - TP.HCM',
    email: 'ha.nguyen@medflow.vn',
    status: 'LOCKED',
    role: 'NURSE',
    department: 'Khoa Tim mạch',
  },
];
