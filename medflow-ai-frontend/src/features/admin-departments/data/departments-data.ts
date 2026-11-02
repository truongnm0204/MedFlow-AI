import type { Department } from '../types/department-update.types';

export const MOCK_DEPARTMENTS: Department[] = [
  {
    code: 'DEP-001',
    name: 'Khoa Nội Tiêu hóa',
    facility: 'MedFlow Cơ sở 1 - Hà Nội',
    status: 'ACTIVE',
  },
  {
    code: 'DEP-002',
    name: 'Khoa Tim mạch',
    facility: 'MedFlow Cơ sở 2 - TP.HCM',
    status: 'INACTIVE',
  },
];
