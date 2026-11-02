import type { PatientDetail } from '../types/patient-detail.types';

// Mock data cho M2 — se duoc thay bang API chi tiet benh nhan.
export const PATIENT_DETAILS_MOCK: PatientDetail[] = [
  {
    id: 'p-0018',
    patientCode: 'BN-2024-0018',
    fullName: 'Nguyễn Văn An',
    gender: 'MALE',
    age: 34,
    dateOfBirth: '14/05/1992',
    cccd: '0010 8801 5708',
    phone: '0912 345 678',
    bhytNumber: 'DN4010123456789',
    address: '25 Nguyễn Huệ, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh',
    emergencyContact: { name: 'Nguyễn Thị Hoa', relationship: 'Vợ', phone: '0987 654 321' },
    todayAppointment: {
      timeRange: '09:00 - 09:30',
      doctorName: 'BS. CKII Trần Khánh',
      room: 'P.102',
      status: 'ARRIVED',
    },
    pastVisits: [
      {
        id: 'v-003',
        date: '12/03/2026',
        department: 'Nội tổng quát',
        doctorName: 'BS. CKII Trần Khánh',
        diagnosis: 'Viêm dạ dày cấp, theo dõi sau điều trị',
      },
      {
        id: 'v-002',
        date: '20/11/2025',
        department: 'Tim mạch',
        doctorName: 'BS. Lê Hoàng Nam',
        diagnosis: 'Tăng huyết áp độ 1',
      },
      {
        id: 'v-001',
        date: '05/06/2025',
        department: 'Tai mũi họng',
        doctorName: 'BS. Phạm Thu Hà',
        diagnosis: 'Viêm họng cấp',
      },
    ],
  },
  {
    id: 'p-0042',
    patientCode: 'BN-2024-0042',
    fullName: 'Trần Thị Bích',
    gender: 'FEMALE',
    age: 67,
    dateOfBirth: '02/09/1959',
    cccd: '0790 5900 1234',
    phone: '0903 111 222',
    bhytNumber: 'HT2790987654321',
    address: '112 Lê Lợi, Phường 4, Quận Gò Vấp, TP. Hồ Chí Minh',
    emergencyContact: { name: 'Trần Văn Bình', relationship: 'Con trai', phone: '0977 888 999' },
    todayAppointment: {
      timeRange: '08:30 - 09:00',
      doctorName: 'BS. CKII Trần Khánh',
      room: 'P.102',
      status: 'IN_PROGRESS',
    },
    pastVisits: [
      {
        id: 'v-101',
        date: '08/01/2026',
        department: 'Nội tiết',
        doctorName: 'BS. Võ Minh Tâm',
        diagnosis: 'Đái tháo đường type 2, kiểm soát đường huyết',
      },
    ],
  },
];

export function findPatientDetailByCode(patientCode: string): PatientDetail | null {
  return PATIENT_DETAILS_MOCK.find((p) => p.patientCode === patientCode) ?? null;
}
