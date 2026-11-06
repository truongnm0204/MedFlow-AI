import type { DoctorWorkloadItem, VisitPatientSummary } from '../types/assign-doctor.types';

export const ASSIGN_DOCTOR_VISIT_MOCK: VisitPatientSummary = {
  visitId: 'VIS-2024-0021',
  fullName: 'Trần Minh Đức',
  gender: 'Nam',
  age: 41,
  patientCode: 'BN-2024-0021',
  cccdVerified: true,
  serviceName: 'Khám Chuyên khoa Tiêu Hóa',
  serviceCode: 'NV-EX-GASTRO',
  price: 250000,
};

export const ASSIGN_DOCTOR_MOCK_DOCTORS: DoctorWorkloadItem[] = [
  {
    id: 'doc-001',
    fullName: 'ThS.BS Nguyễn Văn Hùng',
    department: 'Tiêu Hóa',
    roomName: 'Phòng 206',
    roomLocation: 'Tầng 2, Nhà A',
    waitingCount: 2,
    currentTicket: '007',
    estimatedWait: '~10-15 phút',
    workloadLevel: 'LOW',
  },
  {
    id: 'doc-002',
    fullName: 'BS.CKI Trần Quốc Tuấn',
    department: 'Tiêu Hóa',
    roomName: 'Phòng 204',
    roomLocation: 'Tầng 2, Nhà A',
    waitingCount: 5,
    currentTicket: '010',
    estimatedWait: '~30-45 phút',
    workloadLevel: 'MEDIUM',
  },
  {
    id: 'doc-003',
    fullName: 'BS.CKI Phạm Minh Trí',
    department: 'Tiêu Hóa',
    roomName: 'Phòng 208',
    roomLocation: 'Tầng 2, Nhà A',
    waitingCount: 8,
    currentTicket: '011',
    estimatedWait: null,
    workloadLevel: 'HIGH',
  },
];
