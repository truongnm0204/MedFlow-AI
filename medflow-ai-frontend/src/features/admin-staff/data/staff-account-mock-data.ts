import type {
  StaffAccountItem,
  StaffAccountMetrics,
  StaffAccountStatus,
  StaffRole,
} from '../types/staff-account.types';

export const STAFF_ROLES: StaffRole[] = ['Bác sĩ', 'Kỹ thuật viên', 'Điều dưỡng', 'Nhân viên quầy'];

export const STAFF_STATUS_LABELS: Record<StaffAccountStatus, string> = {
  ACTIVE: 'Đang hoạt động',
  LOCKED: 'Đã khóa',
  DISABLED: 'Đã vô hiệu hóa',
};

export const STAFF_DEPARTMENTS = [
  'Nội tổng quát',
  'Chẩn đoán hình ảnh',
  'Xét nghiệm',
  'Nhi khoa',
  'Tiếp đón',
  'Tim mạch',
];

export const STAFF_ACCOUNT_METRICS: StaffAccountMetrics = {
  total: 56,
  active: 48,
  locked: 5,
  disabled: 3,
};

const FACILITY = 'MedFlow Quận 1';

export const STAFF_ACCOUNTS: StaffAccountItem[] = [
  { id: '1', code: 'STF-0142', fullName: 'Lê Hoàng Minh', email: 'minh.le@medflow.vn', role: 'Bác sĩ', department: 'Nội tổng quát', facility: FACILITY, status: 'ACTIVE' },
  { id: '2', code: 'STF-0089', fullName: 'Trần Hoài Nam', email: 'nam.tran@medflow.vn', role: 'Bác sĩ', department: 'Tim mạch', facility: FACILITY, status: 'ACTIVE' },
  { id: '3', code: 'STF-0185', fullName: 'Phạm Thu Hà', email: 'ha.pham@medflow.vn', role: 'Kỹ thuật viên', department: 'Chẩn đoán hình ảnh', facility: FACILITY, status: 'ACTIVE' },
  { id: '4', code: 'STF-0177', fullName: 'Nguyễn Thị Lan', email: 'lan.nguyen@medflow.vn', role: 'Điều dưỡng', department: 'Nhi khoa', facility: FACILITY, status: 'LOCKED' },
  { id: '5', code: 'STF-0231', fullName: 'Võ Quốc Bảo', email: 'bao.vo@medflow.vn', role: 'Nhân viên quầy', department: 'Tiếp đón', facility: FACILITY, status: 'ACTIVE' },
  { id: '6', code: 'STF-0102', fullName: 'Đặng Minh Châu', email: 'chau.dang@medflow.vn', role: 'Bác sĩ', department: 'Nhi khoa', facility: 'MedFlow Thủ Đức', status: 'DISABLED' },
  { id: '7', code: 'STF-0196', fullName: 'Hoàng Gia Huy', email: 'huy.hoang@medflow.vn', role: 'Kỹ thuật viên', department: 'Xét nghiệm', facility: 'MedFlow Thủ Đức', status: 'ACTIVE' },
  { id: '8', code: 'STF-0203', fullName: 'Bùi Thanh Tâm', email: 'tam.bui@medflow.vn', role: 'Điều dưỡng', department: 'Nội tổng quát', facility: FACILITY, status: 'ACTIVE' },
  { id: '9', code: 'STF-0245', fullName: 'Ngô Khánh Linh', email: 'linh.ngo@medflow.vn', role: 'Nhân viên quầy', department: 'Tiếp đón', facility: 'MedFlow Thủ Đức', status: 'LOCKED' },
  { id: '10', code: 'STF-0118', fullName: 'Đỗ Anh Tuấn', email: 'tuan.do@medflow.vn', role: 'Bác sĩ', department: 'Chẩn đoán hình ảnh', facility: FACILITY, status: 'ACTIVE' },
  { id: '11', code: 'STF-0160', fullName: 'Vũ Ngọc Mai', email: 'mai.vu@medflow.vn', role: 'Điều dưỡng', department: 'Tim mạch', facility: FACILITY, status: 'DISABLED' },
  { id: '12', code: 'STF-0219', fullName: 'Lý Đức Thịnh', email: 'thinh.ly@medflow.vn', role: 'Kỹ thuật viên', department: 'Xét nghiệm', facility: FACILITY, status: 'ACTIVE' },
];
