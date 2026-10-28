import type { FamilyMember } from '../types/family.types';

export const initialMockFamilyMembers: FamilyMember[] = [
  {
    id: 'FAM-001',
    fullName: 'NGUYỄN VĂN AN',
    relationship: 'Bản thân',
    relationshipBadgeColor: 'slate',
    patientCode: '#PAT-2026-8921',
    subStatus: 'Ủy quyền toàn phần',
    isPrimaryAccount: true,
    avatarUrl:
      'https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=300&auto=format&fit=crop&q=80',
    birthDate: '15/08/1988',
    age: 37,
    gender: 'Nam',
    nationality: 'Việt Nam',
    identificationNo: '001088****** (đã che 6 số cuối)',
    identityIssueDate: '20/04/2021',
    identityIssuePlace: 'Cục Cảnh sát QLHC về TTXH',
    isIdentityVerified: true,
    phone: '0912 345 678',
    email: 'nguyenvana.patient@gmail.com',
    address: 'Số 45 Tràng Tiền, Phường Tràng Tiền, Quận Hoàn Kiếm, Thành phố Hà Nội',
    emergencyContactName: 'Nguyễn Thị Bình',
    emergencyContactRelation: 'Vợ',
    emergencyPhone: '0988 765 432',
    outpatientVisitsCount: 4,
    activePrescriptionsCount: 1,
    vitals: {
      bloodType: 'O+',
      heightCm: 172,
      weightKg: 68,
    },
    permissions: {
      canManage: true,
      canBookAppointments: true,
      canViewMedicalRecords: true,
      relationshipStatus: 'ACTIVE',
      consentVerifiedAt: '10/01/2026',
    },
  },
  {
    id: 'FAM-002',
    fullName: 'NGUYỄN MINH KHANG',
    relationship: 'Con trai',
    relationshipBadgeColor: 'blue',
    patientCode: '#PAT-2026-9042',
    subStatus: 'Hồ sơ nhi khoa ngoại trú',
    isPrimaryAccount: false,
    avatarUrl:
      'https://images.unsplash.com/photo-1543332164-6e82f355badc?w=300&auto=format&fit=crop&q=80',
    birthDate: '12/04/2018',
    age: 8,
    gender: 'Nam',
    nationality: 'Việt Nam',
    identificationNo: '001218****** (Mã định danh trẻ em)',
    identityIssueDate: '18/04/2018',
    identityIssuePlace: 'UBND Quận Hoàn Kiếm, TP. Hà Nội',
    isIdentityVerified: true,
    address: 'Số 45 Tràng Tiền, Phường Tràng Tiền, Quận Hoàn Kiếm, Thành phố Hà Nội',
    guardian: 'Bố (Nguyễn Văn An)',
    emergencyContactName: 'Nguyễn Văn An',
    emergencyContactRelation: 'Bố ruột (Giám hộ)',
    emergencyPhone: '0912 345 678',
    latestVisit: '02/09/2026 (Nhi khoa Hô hấp)',
    followUpSchedule: 'Chưa có lịch hẹn mới',
    servicePackage: 'Gói Nhi khoa tiêu chuẩn',
    clinicalNotes: 'Theo dõi viêm mũi dị ứng thời tiết, không dị ứng kháng sinh',
    hasActiveAppointment: false,
    outpatientVisitsCount: 3,
    activePrescriptionsCount: 1,
    vitals: {
      bloodType: 'O+',
      heightCm: 128,
      weightKg: 26,
    },
    notice: {
      type: 'warning',
      message: 'Khám Nhi tự nguyện: Cần có phụ huynh đi cùng khi thăm khám.',
    },
    permissions: {
      canManage: true,
      canBookAppointments: true,
      canViewMedicalRecords: true,
      relationshipStatus: 'ACTIVE',
      consentVerifiedAt: '12/02/2026',
    },
  },
  {
    id: 'FAM-003',
    fullName: 'NGUYỄN THỊ BÌNH',
    relationship: 'Vợ',
    relationshipBadgeColor: 'rose',
    patientCode: '#PAT-2026-8815',
    subStatus: 'Hồ sơ Ngoại trú tiêu chuẩn',
    isPrimaryAccount: false,
    avatarUrl:
      'https://images.unsplash.com/photo-1544005313-94ddf0286df2?w=300&auto=format&fit=crop&q=80',
    birthDate: '20/11/1990',
    age: 35,
    gender: 'Nữ',
    nationality: 'Việt Nam',
    identificationNo: '001190****** (đã che 6 số cuối)',
    identityIssueDate: '15/05/2021',
    identityIssuePlace: 'Cục Cảnh sát QLHC về TTXH',
    isIdentityVerified: true,
    phone: '0988 765 432',
    email: 'nguyenthibinh.90@gmail.com',
    address: 'Số 45 Tràng Tiền, Phường Tràng Tiền, Quận Hoàn Kiếm, Thành phố Hà Nội',
    emergencyContactName: 'Nguyễn Văn An',
    emergencyContactRelation: 'Chồng',
    emergencyPhone: '0912 345 678',
    latestVisit: '14/01/2026 (Nhi khoa / Da liễu)',
    followUpSchedule: 'Chưa đặt lịch mới',
    servicePackage: 'Khám lẻ Ngoại trú',
    clinicalNotes: 'Sức khỏe tổng quát ổn định, kiểm tra định kỳ 6 tháng/lần',
    hasActiveAppointment: true,
    activeAppointmentDetails: '25/11/2026 (Khám Sản phụ khoa - BS. Nguyễn Thu Hà)',
    outpatientVisitsCount: 2,
    activePrescriptionsCount: 0,
    vitals: {
      bloodType: 'A+',
      heightCm: 160,
      weightKg: 52,
    },
    permissions: {
      canManage: true,
      canBookAppointments: true,
      canViewMedicalRecords: true,
      relationshipStatus: 'ACTIVE',
      consentVerifiedAt: '15/01/2026',
    },
  },
  {
    id: 'FAM-004',
    fullName: 'LÊ THỊ MAI',
    relationship: 'Mẹ ruột',
    relationshipBadgeColor: 'amber',
    patientCode: '#PAT-2026-7230',
    subStatus: 'Hồ sơ Lão khoa & Tim mạch',
    isPrimaryAccount: false,
    avatarUrl:
      'https://images.unsplash.com/photo-1551836022-d5d88e9218df?w=300&auto=format&fit=crop&q=80',
    birthDate: '05/02/1960',
    age: 66,
    gender: 'Nữ',
    nationality: 'Việt Nam',
    identificationNo: '001160****** (đã che 6 số cuối)',
    identityIssueDate: '10/08/2021',
    identityIssuePlace: 'Cục Cảnh sát QLHC về TTXH',
    isIdentityVerified: true,
    phone: '0913 222 333',
    address: 'Số 12 Lý Thường Kiệt, Quận Hoàn Kiếm, Thành phố Hà Nội',
    guardian: 'Con trai (Nguyễn Văn An)',
    emergencyContactName: 'Nguyễn Văn An',
    emergencyContactRelation: 'Con trai',
    emergencyPhone: '0912 345 678',
    latestVisit: '20/08/2026 (Nội Tim mạch)',
    followUpSchedule: '20/11/2026 (Tái khám định kỳ)',
    servicePackage: 'Gói Chăm sóc Tim mạch & Lão khoa',
    clinicalNotes: 'Huyết áp, Khớp thoái hóa',
    outpatientVisitsCount: 6,
    activePrescriptionsCount: 2,
    vitals: {
      bloodType: 'B+',
      heightCm: 155,
      weightKg: 54,
    },
    notice: {
      type: 'info',
      message: 'Ủy quyền nhận kết quả xét nghiệm & đơn thuốc ngoại trú qua SMS.',
    },
    permissions: {
      canManage: false,
      canBookAppointments: true,
      canViewMedicalRecords: true,
      relationshipStatus: 'ACTIVE',
      consentVerifiedAt: '01/03/2026',
    },
  },
];

export const mockFamilyMembers: FamilyMember[] = [...initialMockFamilyMembers];

const STORAGE_KEY = 'medflow.mock_family_members';

export function getStoredFamilyMembers(): FamilyMember[] {
  if (typeof window === 'undefined') {
    return mockFamilyMembers;
  }
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(mockFamilyMembers));
      return mockFamilyMembers;
    }
    const parsed = JSON.parse(raw) as FamilyMember[];
    if (!Array.isArray(parsed) || parsed.length === 0) {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(mockFamilyMembers));
      return mockFamilyMembers;
    }
    mockFamilyMembers.length = 0;
    mockFamilyMembers.push(...parsed);
    return parsed;
  } catch {
    return mockFamilyMembers;
  }
}

export function saveStoredFamilyMembers(members: FamilyMember[]): void {
  mockFamilyMembers.length = 0;
  mockFamilyMembers.push(...members);
  if (typeof window !== 'undefined') {
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(members));
    } catch {
      // ignore
    }
  }
}

export function resetStoredFamilyMembers(): void {
  saveStoredFamilyMembers([...initialMockFamilyMembers]);
}

export function getFamilyMemberById(id: string): FamilyMember | undefined {
  const normalizedId = id?.trim().toLowerCase();
  const list = getStoredFamilyMembers();
  return list.find((m) => m.id.trim().toLowerCase() === normalizedId);
}
