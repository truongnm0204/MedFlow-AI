import type {
  PatientHomeSummary,
  PatientQuickAction,
  QuickSlotItem,
  QuickSlotSearchRequest,
} from '../types/home.types';

/** Danh sách lối vào chức năng được phép hiển thị cho vai trò PATIENT ở M1. */
export const PATIENT_QUICK_ACTIONS: PatientQuickAction[] = [
  {
    id: 'book-appointment',
    label: 'Đặt lịch khám mới',
    description: 'Chọn chuyên khoa, bác sĩ và khung giờ khám ngoại trú tự nguyện',
    href: '#booking',
    badgeText: 'Còn slot hôm nay',
    accent: 'sky',
  },
  {
    id: 'search-doctor',
    label: 'Tìm bác sĩ & dịch vụ',
    description: 'Tra cứu đội ngũ Tiến sĩ, BS.CKII và bảng giá niêm yết công khai',
    href: '#doctors',
    accent: 'emerald',
  },
  {
    id: 'personal-profile',
    label: 'Hồ sơ cá nhân',
    description: 'Xem và cập nhật thông tin liên hệ, định danh y tế và sinh hiệu',
    href: '/profile',
    accent: 'indigo',
  },
  {
    id: 'family-profiles',
    label: 'Hồ sơ người thân',
    description: 'Quản lý danh sách thành viên gia đình để đặt lịch khám hộ',
    href: '/family',
    badgeText: 'Tối đa 5 hồ sơ',
    accent: 'amber',
  },
];

export const MOCK_PATIENT_HOME_SUMMARY: PatientHomeSummary = {
  patientCode: 'PAT-2026-0001',
  fullName: 'Nguyễn Văn An',
  isVerified: true,
  metrics: {
    upcomingAppointmentsCount: 1,
    familyMembersCount: 3,
    completedVisitsCount: 4,
    activePrescriptionsCount: 1,
  },
  upcomingAppointments: [
    {
      id: 'APT-2026-0108',
      appointmentCode: 'MF-261028-08',
      specialtyName: 'Nội Tiêu Hóa & Nội Soi',
      doctorName: 'TS. BS. Nguyễn Văn Hùng',
      roomName: 'Phòng khám P.102 (Tầng 1)',
      scheduledDate: 'Hôm nay',
      scheduledTime: '09:30 - 10:00',
      patientName: 'Nguyễn Văn An (Bản thân)',
      status: 'CONFIRMED',
    },
  ],
  lastUpdatedLabel: 'Đồng bộ từ Cổng Bệnh nhân MedFlow',
};

export const MOCK_QUICK_SLOTS: Record<string, QuickSlotItem[]> = {
  gastro: [
    {
      id: 'slot-gastro-1',
      timeRange: '08:45 - 09:15',
      doctorName: 'TS. BS. Nguyễn Văn Hùng',
      specialtyName: 'Nội Tiêu Hóa & Nội Soi',
      roomName: 'P.102',
      feeLabel: '350.000đ',
      availableSeats: 2,
    },
    {
      id: 'slot-gastro-2',
      timeRange: '10:15 - 10:45',
      doctorName: 'TS. BS. Nguyễn Văn Hùng',
      specialtyName: 'Nội Tiêu Hóa & Nội Soi',
      roomName: 'P.102',
      feeLabel: '350.000đ',
      availableSeats: 4,
    },
  ],
  cardio: [
    {
      id: 'slot-cardio-1',
      timeRange: '09:15 - 09:45',
      doctorName: 'BS. CKII Trần Mai Anh',
      specialtyName: 'Tim Mạch Can Thiệp',
      roomName: 'P.205',
      feeLabel: '400.000đ',
      availableSeats: 3,
    },
  ],
  ent: [
    {
      id: 'slot-ent-1',
      timeRange: '10:00 - 10:30',
      doctorName: 'ThS. BS. Lê Thu Hà',
      specialtyName: 'Tai Mũi Họng',
      roomName: 'P.301',
      feeLabel: '300.000đ',
      availableSeats: 5,
    },
  ],
  pediatrics: [
    {
      id: 'slot-ped-1',
      timeRange: '14:00 - 14:30',
      doctorName: 'BS. CKI Phạm Quang Minh',
      specialtyName: 'Nhi Khoa',
      roomName: 'P.108',
      feeLabel: '350.000đ',
      availableSeats: 3,
    },
  ],
  ortho: [
    {
      id: 'slot-ortho-1',
      timeRange: '15:00 - 15:30',
      doctorName: 'TS. BS. Nguyễn Văn Hùng',
      specialtyName: 'Cơ Xương Khớp',
      roomName: 'P.204',
      feeLabel: '350.000đ',
      availableSeats: 2,
    },
  ],
  eye: [],
};

export function filterMockQuickSlots(req: QuickSlotSearchRequest): QuickSlotItem[] {
  const list = MOCK_QUICK_SLOTS[req.specialtyId] ?? [];
  if (req.doctorId === 'all') return list;

  const doctorKeywordMap: Record<string, string> = {
    doc1: 'Nguyễn Văn Hùng',
    doc2: 'Trần Mai Anh',
    doc3: 'Lê Thu Hà',
    doc4: 'Phạm Quang Minh',
  };
  const keyword = doctorKeywordMap[req.doctorId];
  if (!keyword) return list;
  return list.filter((slot) => slot.doctorName.includes(keyword));
}
