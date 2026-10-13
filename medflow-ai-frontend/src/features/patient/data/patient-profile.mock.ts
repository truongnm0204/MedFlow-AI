import type { PatientProfile } from '../types/patient-profile.types';

export const mockPatientProfile: PatientProfile = {
  id: 'PAT-20261013-0001',
  avatarUrl:
    'https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=300&auto=format&fit=crop&q=80',
  identity: {
    fullName: 'NGUYỄN VĂN AN',
    dateOfBirth: '15/08/1988',
    age: 37,
    gender: 'Nam',
    nationality: 'Việt Nam',
    identityNumber: '001088****** (đã che 6 số cuối)',
    identityIssueDate: '20/04/2021',
    identityIssuePlace: 'Cục Cảnh sát QLHC về TTXH',
    isIdentityVerified: true,
    pidNumber: 'PID-1988-001088',
  },
  vitals: {
    bloodType: 'O+',
    heightCm: 172,
    weightKg: 68,
  },
  shortcuts: {
    familyMembersCount: 3,
    outpatientVisitsCount: 4,
    activePrescriptionsCount: 1,
  },
  contact: {
    phone: '0912 345 678',
    isPhoneVerified: true,
    email: 'nguyenvana.patient@gmail.com',
    isEmailVerified: true,
    permanentAddress: 'Số 45 Tràng Tiền, Phường Tràng Tiền, Quận Hoàn Kiếm, Thành phố Hà Nội',
    emergencyContact: {
      fullName: 'Nguyễn Thị Bình',
      relationship: 'Vợ',
      phone: '0988 765 432',
    },
  },
};
