import type { WalkInDepartment, WalkInPatient } from '../types/walk-in-visit.types';

export const WALK_IN_PATIENT_MOCK: WalkInPatient = {
  id: 'pat-0021',
  patientCode: 'BN-2024-0021',
  fullName: 'Trần Minh Đức',
  gender: 'MALE',
  birthYear: 1985,
  identityCard: '001085002021',
  phoneNumber: '0936 888 999',
  insuranceType: 'KHÁM NGOẠI TRÚ TỰ NGUYỆN (100% KHÔNG BHYT)',
};

export const WALK_IN_DEPARTMENTS_MOCK: WalkInDepartment[] = [
  {
    id: 'dept-gastro',
    name: 'Khoa Tiêu hóa - Gan Mật',
    location: 'Tầng 2, Nhà A',
    services: [
      {
        id: 'srv-gastro-01',
        code: 'NV-EX-GASTRO',
        name: 'Khám Chuyên khoa Tiêu Hóa Tự Nguyện',
        price: 250000,
        priceFormatted: '250.000 VNĐ',
      },
      {
        id: 'srv-gastro-02',
        code: 'NV-EX-ENDO-GASTRO',
        name: 'Khám & Tư vấn Nội soi Tiêu hóa',
        price: 350000,
        priceFormatted: '350.000 VNĐ',
      },
    ],
  },
  {
    id: 'dept-cardio',
    name: 'Khoa Tim mạch',
    location: 'Tầng 1, Nhà B',
    services: [
      {
        id: 'srv-cardio-01',
        code: 'NV-EX-CARDIO',
        name: 'Khám Chuyên khoa Tim mạch Tự Nguyện',
        price: 300000,
        priceFormatted: '300.000 VNĐ',
      },
    ],
  },
  {
    id: 'dept-ent',
    name: 'Khoa Tai Mũi Họng',
    location: 'Tầng 2, Nhà B',
    services: [
      {
        id: 'srv-ent-01',
        code: 'NV-EX-ENT',
        name: 'Khám Chuyên khoa Tai Mũi Họng Tự Nguyện',
        price: 200000,
        priceFormatted: '200.000 VNĐ',
      },
    ],
  },
  {
    id: 'dept-endo',
    name: 'Khoa Nội tiết',
    location: 'Tầng 2, Nhà A',
    services: [
      {
        id: 'srv-endo-01',
        code: 'NV-EX-ENDO',
        name: 'Khám Chuyên khoa Nội tiết - Đái tháo đường',
        price: 250000,
        priceFormatted: '250.000 VNĐ',
      },
    ],
  },
];

export const SYMPTOM_OPTIONS_MOCK: string[] = [
  'Đau thượng vị',
  'Ợ hơi/ợ chua',
  'Đầy bụng khó tiêu',
  'Buồn nôn',
  'Đi ngoài phân đen',
  'Đau tức ngực',
  'Chóng mặt',
  'Sốt nhẹ',
];
