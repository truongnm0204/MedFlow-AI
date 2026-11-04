import type {
  QueueEntry,
  QuickAction,
  ReceptionStats,
  TodayAppointment,
} from '../types/reception.types';
import type { UpdatePatientFormData } from '../types/update-patient.types';
import type { CompatibleExaminationRoomItemDto } from '../types/visit-room-transfer.types';
import type {
  ExaminationTicketDto,
  PrintExaminationTicketRequest,
} from '../types/visit-ticket.types';

// Mock data cho M1 role-landing — se duoc thay bang API M2.
export const RECEPTION_STATS_MOCK: ReceptionStats = {
  waitingCheckIn: 8,
  inQueue: 14,
  inConsultation: 5,
  completedToday: 32,
};

export const QUEUE_ENTRIES_MOCK: QueueEntry[] = [
  {
    id: 'q-001',
    visitCode: 'VIS-202610-0001',
    ticketNo: 'A012',
    patientName: 'Nguyễn Văn An',
    patientCode: 'BN-2024-0018',
    visitType: 'SCHEDULED',
    room: 'P.102',
    doctorName: 'BS.CKI Nguyễn Minh Đức',
    departmentName: 'Khoa Tiêu hóa',
    status: 'IN_PROGRESS',
    checkedInAt: '08:02',
    priority: 'NORMAL',
  },
  {
    id: 'q-002',
    visitCode: 'VIS-202610-0002',
    ticketNo: 'A013',
    patientName: 'Trần Thị Bích',
    patientCode: 'BN-2024-0042',
    visitType: 'SCHEDULED',
    room: 'P.102',
    doctorName: 'ThS.BS Trần Thu Hà',
    departmentName: 'Khoa Tim mạch',
    status: 'CALLED',
    checkedInAt: '08:15',
    priority: 'ELDERLY',
  },
  {
    id: 'q-003',
    visitCode: 'VIS-202610-0009',
    ticketNo: 'A014',
    patientName: 'Lê Minh Cường',
    patientCode: 'BN-2024-0071',
    visitType: 'WALK_IN',
    room: 'P.103',
    doctorName: 'BS.CKI Nguyễn Minh Đức',
    departmentName: 'Khoa Tiêu hóa',
    status: 'WAITING',
    checkedInAt: '08:22',
    priority: 'NORMAL',
  },
  {
    id: 'q-004',
    visitCode: 'VIS-202610-0004',
    ticketNo: 'A015',
    patientName: 'Phạm Thu Dung',
    patientCode: 'BN-2024-0089',
    visitType: 'SCHEDULED',
    room: 'P.102',
    doctorName: 'ThS.BS Trần Thu Hà',
    departmentName: 'Khoa Tim mạch',
    status: 'WAITING',
    checkedInAt: '08:31',
    priority: 'PREGNANT',
  },
  {
    id: 'q-005',
    visitCode: 'VIS-202610-0005',
    ticketNo: 'A016',
    patientName: 'Hoàng Đức Em',
    patientCode: 'BN-2024-0103',
    visitType: 'WALK_IN',
    room: 'P.104',
    doctorName: 'BS.CKI Nguyễn Minh Đức',
    departmentName: 'Khoa Tiêu hóa',
    status: 'WAITING',
    checkedInAt: '08:40',
    priority: 'URGENT',
  },
  {
    id: 'q-006',
    visitCode: 'VIS-202610-0006',
    ticketNo: 'A017',
    patientName: 'Vũ Thị Phương',
    patientCode: 'BN-2024-0117',
    visitType: 'SCHEDULED',
    room: 'P.103',
    doctorName: 'ThS.BS Trần Thu Hà',
    departmentName: 'Khoa Tim mạch',
    status: 'WAITING',
    checkedInAt: '08:47',
    priority: 'NORMAL',
  },
];

export const TODAY_APPOINTMENTS_MOCK: TodayAppointment[] = [
  {
    id: 'ap-001',
    time: '09:00',
    patientName: 'Đỗ Gia Huy',
    doctorName: 'BS. Trần Khánh',
    department: 'Nội tổng quát',
    status: 'CHECKED_IN',
  },
  {
    id: 'ap-002',
    time: '09:15',
    patientName: 'Ngô Thị Lan',
    doctorName: 'BS. Trần Khánh',
    department: 'Nội tổng quát',
    status: 'BOOKED',
  },
  {
    id: 'ap-003',
    time: '09:30',
    patientName: 'Bùi Quốc Nam',
    doctorName: 'BS. Lê Hạnh',
    department: 'Da liễu',
    status: 'BOOKED',
  },
  {
    id: 'ap-004',
    time: '09:45',
    patientName: 'Phan Thảo Nguyên',
    doctorName: 'BS. Trần Khánh',
    department: 'Nội tổng quát',
    status: 'BOOKED',
  },
  {
    id: 'ap-005',
    time: '10:00',
    patientName: 'Trịnh Văn Phát',
    doctorName: 'BS. Đinh Long',
    department: 'Tai mũi họng',
    status: 'BOOKED',
  },
];

export const QUICK_ACTIONS_MOCK: QuickAction[] = [
  {
    id: 'check-in',
    label: 'Check-in bệnh nhân hẹn',
    description: 'Quét mã hoặc tìm lịch hẹn để tiếp nhận',
    icon: 'M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-6 9l2 2 4-4',
    accent: 'sky',
  },
  {
    id: 'walk-in',
    label: 'Tạo lượt khám Walk-in',
    description: 'Bệnh nhân đến trực tiếp không hẹn trước',
    icon: 'M13 5l7 7-7 7M5 5l7 7-7 7',
    accent: 'emerald',
  },
  {
    id: 'new-patient',
    label: 'Tạo hồ sơ bệnh nhân',
    description: 'Đăng ký hồ sơ mới tại quầy lễ tân',
    icon: 'M18 9v3m0 0v3m0-3h3m-3 0h-3m-2-5a4 4 0 11-8 0 4 4 0 018 0zM3 20a6 6 0 0112 0v1H3v-1z',
    accent: 'rose',
  },
  {
    id: 'search',
    label: 'Tra cứu bệnh nhân',
    description: 'Tìm theo mã BN, SĐT hoặc họ tên',
    icon: 'M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z',
    accent: 'amber',
  },
  {
    id: 'update-patient',
    label: 'Cập nhật hồ sơ bệnh nhân',
    description: 'Chỉnh sửa thông tin hành chính, BHYT, liên hệ',
    icon: 'M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z',
    accent: 'sky',
  },
  {
    id: 'transfer-room',
    label: 'Chuyển phòng khám tương thích',
    description: 'Điều chuyển bệnh nhân sang phòng khám cùng khoa còn trống',
    icon: 'M8 7h12m0 0l-4-4m4 4l-4 4m0 6H4m0 0l4 4m-4-4l4-4',
    accent: 'emerald',
  },
  {
    id: 'print-ticket',
    label: 'In phiếu khám bệnh',
    description: 'Xem trước và in phiếu khám ngoại trú (UC29)',
    icon: 'M17 17h2a2 2 0 002-2v-4a2 2 0 00-2-2H5a2 2 0 00-2 2v4a2 2 0 002 2h2m2 4h6a2 2 0 002-2v-4a2 2 0 00-2-2H9a2 2 0 00-2 2v4a2 2 0 002 2zm8-12V5a2 2 0 00-2-2H9a2 2 0 00-2 2v4h10z',
    accent: 'emerald',
  },
];

export const PATIENT_UPDATE_MOCK: UpdatePatientFormData = {
  patientCode: 'BN-2024-0018',
  fullName: 'Nguyễn Văn An',
  dateOfBirth: '1985-03-12',
  gender: 'MALE',
  ethnicity: 'Kinh',
  nationalId: '079085001234',
  phone: '0912345678',
  email: 'an.nguyen@example.com',
  address: '12 Nguyễn Huệ, Quận 1, TP.HCM',
  insuranceNumber: 'DN4797912345678',
  insuranceFacility: 'Bệnh viện Quận 1',
  emergencyContactName: 'Nguyễn Thị Lan',
  emergencyContactRelationship: 'Vợ',
  emergencyContactPhone: '0987654321',
};

export const COMPATIBLE_TRANSFER_ROOMS_MOCK: CompatibleExaminationRoomItemDto[] = [
  {
    roomId: 1,
    roomCode: 'RM-001',
    roomName: 'Khám Tiêu hóa 101',
    hospitalId: 1,
    departmentId: 2,
    departmentCode: 'GI',
    departmentName: 'Khoa Tiêu hóa',
    roomType: 'EXAMINATION',
    location: 'Tầng 1, Khu A',
    capacity: 2,
    activeVisitCount: 2,
    availableCapacity: 0,
    status: 'ACTIVE',
    isCurrentRoom: true,
    isCompatible: true,
    isAvailable: false,
    unavailableReason: 'Đang là phòng khám hiện tại của bệnh nhân.',
  },
  {
    roomId: 2,
    roomCode: 'RM-002',
    roomName: 'Khám Tiêu hóa 102',
    hospitalId: 1,
    departmentId: 2,
    departmentCode: 'GI',
    departmentName: 'Khoa Tiêu hóa',
    roomType: 'EXAMINATION',
    location: 'Tầng 1, Khu A',
    capacity: 2,
    activeVisitCount: 1,
    availableCapacity: 1,
    status: 'ACTIVE',
    isCurrentRoom: false,
    isCompatible: true,
    isAvailable: true,
    unavailableReason: null,
  },
  {
    roomId: 6,
    roomCode: 'RM-006',
    roomName: 'Khám Tiêu hóa 103 (Bảo trì)',
    hospitalId: 1,
    departmentId: 2,
    departmentCode: 'GI',
    departmentName: 'Khoa Tiêu hóa',
    roomType: 'EXAMINATION',
    location: 'Tầng 1, Khu A',
    capacity: 2,
    activeVisitCount: 0,
    availableCapacity: 0,
    status: 'MAINTENANCE',
    isCurrentRoom: false,
    isCompatible: true,
    isAvailable: false,
    unavailableReason: 'Phòng khám hiện không hoạt động hoặc đang bảo trì.',
  },
  {
    roomId: 3,
    roomCode: 'RM-003',
    roomName: 'Khám Tim mạch 201',
    hospitalId: 1,
    departmentId: 3,
    departmentCode: 'CARD',
    departmentName: 'Khoa Tim mạch',
    roomType: 'EXAMINATION',
    location: 'Tầng 2, Khu A',
    capacity: 2,
    activeVisitCount: 0,
    availableCapacity: 2,
    status: 'ACTIVE',
    isCurrentRoom: false,
    isCompatible: false,
    isAvailable: false,
    unavailableReason:
      'Phòng khám đích không tương thích chuyên khoa/khoa phòng với lượt khám hiện tại.',
  },
];
const FALLBACK_PRINT_COUNTS: Record<string, number> = {};

export function buildFallbackExaminationTicket(
  idOrCode: string,
  request?: PrintExaminationTicketRequest,
  incrementPrint = false,
): ExaminationTicketDto {
  const cleanKey = idOrCode.replace(/^#/, '').trim().toUpperCase();
  const matchedEntry =
    QUEUE_ENTRIES_MOCK.find(
      (entry) =>
        entry.visitCode?.toUpperCase() === cleanKey ||
        entry.ticketNo.toUpperCase() === cleanKey ||
        entry.patientCode.toUpperCase() === cleanKey ||
        entry.id.toUpperCase() === cleanKey,
    ) ?? QUEUE_ENTRIES_MOCK[0];

  const visitCode = matchedEntry.visitCode ?? 'VIS-202610-0001';
  const queueNumber = Number.parseInt(matchedEntry.ticketNo.replace(/\D/g, ''), 10) || 1;
  const queueNumberFormatted = String(queueNumber).padStart(3, '0');
  const currentCount = FALLBACK_PRINT_COUNTS[visitCode] ?? 0;
  const nextCount = incrementPrint ? currentCount + 1 : currentCount;
  if (incrementPrint) {
    FALLBACK_PRINT_COUNTS[visitCode] = nextCount;
  }

  const copies = request?.copies ?? 1;
  const isReprint = incrementPrint
    ? currentCount > 0 || Boolean(request?.isReprint)
    : currentCount > 0;
  const isWalkIn = matchedEntry.visitType === 'WALK_IN';

  return {
    visitId: queueNumber,
    visitCode,
    ticketCode: `TKT-${visitCode}`,
    ticketNo: matchedEntry.ticketNo,
    queueNumber,
    queueNumberFormatted,
    queueEntryId: queueNumber,
    queueDate: '2026-10-15',
    queueStatus: matchedEntry.status === 'DONE' ? 'COMPLETED' : matchedEntry.status,
    priority: matchedEntry.priority === 'URGENT' ? 'URGENT' : 'NORMAL',
    priorityLabel:
      matchedEntry.priority === 'URGENT'
        ? 'Ưu tiên khẩn'
        : matchedEntry.priority === 'ELDERLY'
          ? 'Ưu tiên (Người cao tuổi)'
          : 'Thông thường',
    waitingAheadCount: matchedEntry.status === 'WAITING' ? 1 : 0,
    estimatedWaitMinutes: matchedEntry.status === 'WAITING' ? 15 : 0,
    visitType: matchedEntry.visitType,
    visitTypeLabel: isWalkIn ? 'Khám vãng lai (Walk-in)' : 'Khám theo lịch hẹn',
    visitStatus: 'CHECKED_IN',
    visitStatusLabel: 'Đã tiếp nhận',
    appointmentId: isWalkIn ? null : 1,
    appointmentCode: isWalkIn ? null : 'APT-202610-0001',
    scheduledTimeRange: isWalkIn ? null : '09:00 - 09:30',
    patientId: 1,
    patientCode: matchedEntry.patientCode,
    patientName: matchedEntry.patientName,
    patientDateOfBirth: '1980-02-02',
    patientBirthYear: 1980,
    patientAge: 46,
    patientGender: 'MALE',
    patientGenderLabel: 'Nam',
    patientPhone: '0900000001',
    hospitalId: 1,
    hospitalCode: 'HOS-DEMO-01',
    hospitalName: 'Bệnh viện Đa khoa Quốc tế MedFlow',
    hospitalAddress: 'Số 1 Đường Demo, Hà Nội',
    departmentId: 2,
    departmentCode: 'GI',
    departmentName: matchedEntry.departmentName ?? 'Khoa Tiêu hóa',
    specialtyId: 1,
    specialtyCode: 'GI',
    specialtyName: 'Tiêu hóa',
    roomId: 1,
    roomCode: matchedEntry.room.replace('P.', 'RM-'),
    roomName: `Phòng khám ${matchedEntry.room}`,
    roomLocation: 'Tầng 1, Khu Khám bệnh Ngoại trú',
    doctorId: 1,
    doctorCode: 'DOC-001',
    doctorTitle: 'BS.CKI',
    doctorName: matchedEntry.doctorName ?? 'BS.CKI Nguyễn Minh Đức',
    serviceId: 1,
    serviceCode: 'EX-GI-01',
    serviceName: 'Khám Tiêu hóa lần đầu',
    reasonForVisit: 'Đau thượng vị, theo dõi tiêu hóa ngoại trú',
    checkedInAt: `2026-10-15T${matchedEntry.checkedInAt}:00`,
    checkedInAtLabel: `15/10/2026 ${matchedEntry.checkedInAt}`,
    checkedInByStaffId: 3,
    checkedInByStaffName: 'Lễ tân Lê Lan',
    printedAtUtc: '2026-10-15T02:00:00Z',
    printedAtLabel: `15/10/2026 ${matchedEntry.checkedInAt}`,
    printedByUserId: 5,
    printedByStaffCode: 'STF-003',
    printedByStaffName: 'Lễ tân Lê Lan',
    copies,
    printCount: nextCount,
    isReprint,
    guidanceNote: `Vui lòng di chuyển đến Phòng khám ${matchedEntry.room} (Tầng 1, Khu Khám bệnh Ngoại trú) và theo dõi màn hình gọi số thứ tự ${matchedEntry.ticketNo}.`,
  };
}
