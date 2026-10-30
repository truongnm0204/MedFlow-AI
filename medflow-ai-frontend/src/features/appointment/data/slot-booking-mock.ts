import { MOCK_DOCTORS } from './doctor-search-mock';
import { MOCK_EXAMINATION_SERVICES } from './service-search-mock';
import type {
  AvailableSlotsPayload,
  BookingDateOption,
  BookingDoctorServiceInfo,
  BookingPatientProfile,
  BookingServiceOnlyInfo,
  BookingTimeSlot,
  DoctorDayShiftSchedule,
  ServiceAvailableShiftsPayload,
  ServiceShiftOption,
} from '../types/slot-booking.types';

export const MOCK_BOOKING_PROFILES: BookingPatientProfile[] = [
  {
    id: 'pat-self',
    fullName: 'Nguyễn Văn An',
    relationshipLabel: '(Bản thân)',
    badgeText: 'Chính chủ',
    badgeVariant: 'primary',
    gender: 'Nam',
    age: 40,
    metaPrimary: 'SĐT: 0912 345 678',
    metaSecondary: 'Định danh đã đối chiếu',
    patientCode: 'PAT-8821',
  },
  {
    id: 'pat-child',
    fullName: 'Nguyễn Bảo Châu',
    relationshipLabel: '(Con gái)',
    badgeText: 'Nhi khoa',
    badgeVariant: 'pediatric',
    gender: 'Nữ',
    age: 8,
    metaPrimary: 'Hồ sơ Nhi ngoại trú',
    metaSecondary: 'PID: PAT-9012',
    patientCode: 'PAT-9012',
  },
  {
    id: 'pat-mother',
    fullName: 'Lê Thị Mai',
    relationshipLabel: '(Mẹ ruột)',
    badgeText: 'Lão khoa',
    badgeVariant: 'geriatric',
    gender: 'Nữ',
    age: 68,
    metaPrimary: 'Hồ sơ Ngoại trú Định kỳ',
    metaSecondary: 'PID: PAT-4481',
    patientCode: 'PAT-4481',
  },
];

export const MOCK_BOOKING_DATES: BookingDateOption[] = [
  {
    dateIso: '2026-10-12',
    dayTopLabel: 'Thứ 2',
    dayNumber: '12',
    subDateLabel: '12 Thg 10',
    fullDateDisplay: 'Thứ Hai, 12/10/2026',
    status: 'FULL',
    remainingSlots: 0,
    badgeText: 'Hết chỗ',
    doctorShiftSummary: 'Ca Sáng & Chiều',
  },
  {
    dateIso: '2026-10-13',
    dayTopLabel: 'Hôm nay',
    dayNumber: '13',
    subDateLabel: '13 Thg 10',
    fullDateDisplay: 'Thứ Ba, 13/10/2026',
    status: 'AVAILABLE',
    remainingSlots: 9,
    badgeText: 'Còn 9 slot',
    isToday: true,
    doctorShiftSummary: 'Trực cả ngày',
  },
  {
    dateIso: '2026-10-14',
    dayTopLabel: 'Thứ 4',
    dayNumber: '14',
    subDateLabel: 'Ngày mai',
    fullDateDisplay: 'Thứ Tư, 14/10/2026',
    status: 'AVAILABLE',
    remainingSlots: 6,
    badgeText: 'Ca Chiều (6 slot)',
    doctorShiftSummary: 'Chỉ trực Ca Chiều',
  },
  {
    dateIso: '2026-10-15',
    dayTopLabel: 'Thứ 5',
    dayNumber: '15',
    subDateLabel: '15 Thg 10',
    fullDateDisplay: 'Thứ Năm, 15/10/2026',
    status: 'AVAILABLE',
    remainingSlots: 6,
    badgeText: 'Ca Sáng (6 slot)',
    doctorShiftSummary: 'Chỉ trực Ca Sáng',
  },
  {
    dateIso: '2026-10-16',
    dayTopLabel: 'Thứ 6',
    dayNumber: '16',
    subDateLabel: '16 Thg 10',
    fullDateDisplay: 'Thứ Sáu, 16/10/2026',
    status: 'AVAILABLE',
    remainingSlots: 14,
    badgeText: 'Còn 14 slot',
    doctorShiftSummary: 'Trực cả ngày',
  },
  {
    dateIso: '2026-10-17',
    dayTopLabel: 'Thứ 7',
    dayNumber: '17',
    subDateLabel: '17 Thg 10',
    fullDateDisplay: 'Thứ Bảy, 17/10/2026',
    status: 'FEW_SLOTS',
    remainingSlots: 4,
    badgeText: 'Còn 4 slot',
    doctorShiftSummary: 'Trực Ca Sáng',
  },
  {
    dateIso: '2026-10-18',
    dayTopLabel: 'Chủ Nhật',
    dayNumber: '18',
    subDateLabel: '18 Thg 10',
    fullDateDisplay: 'Chủ Nhật, 18/10/2026',
    status: 'OFF',
    remainingSlots: 0,
    badgeText: 'Nghỉ khám định kỳ',
    doctorShiftSummary: 'Nghỉ trực',
  },
];

export const MOCK_MORNING_SLOTS: BookingTimeSlot[] = [
  {
    id: 'slot-0730',
    timeRange: '07:30 - 08:00',
    shift: 'MORNING',
    shiftLabel: 'Ca Sáng',
    status: 'FULL',
    remainingSlots: 0,
    badgeText: 'Hết chỗ',
  },
  {
    id: 'slot-0800',
    timeRange: '08:00 - 08:30',
    shift: 'MORNING',
    shiftLabel: 'Ca Sáng',
    status: 'FULL',
    remainingSlots: 0,
    badgeText: 'Hết chỗ',
  },
  {
    id: 'slot-0830',
    timeRange: '08:30 - 09:00',
    shift: 'MORNING',
    shiftLabel: 'Ca Sáng',
    status: 'AVAILABLE',
    remainingSlots: 2,
    badgeText: 'Còn 2 slot',
  },
  {
    id: 'slot-0900',
    timeRange: '09:00 - 09:30',
    shift: 'MORNING',
    shiftLabel: 'Ca Sáng',
    status: 'AVAILABLE',
    remainingSlots: 3,
    badgeText: 'Còn 3 slot',
  },
  {
    id: 'slot-0930',
    timeRange: '09:30 - 10:00',
    shift: 'MORNING',
    shiftLabel: 'Ca Sáng',
    status: 'AVAILABLE',
    remainingSlots: 3,
    badgeText: 'Còn 3 slot',
  },
  {
    id: 'slot-1000',
    timeRange: '10:00 - 10:30',
    shift: 'MORNING',
    shiftLabel: 'Ca Sáng',
    status: 'AVAILABLE',
    remainingSlots: 4,
    badgeText: 'Còn 4 slot',
  },
  {
    id: 'slot-1030',
    timeRange: '10:30 - 11:00',
    shift: 'MORNING',
    shiftLabel: 'Ca Sáng',
    status: 'AVAILABLE',
    remainingSlots: 2,
    badgeText: 'Còn 2 slot',
  },
  {
    id: 'slot-1100',
    timeRange: '11:00 - 11:30',
    shift: 'MORNING',
    shiftLabel: 'Ca Sáng',
    status: 'AVAILABLE',
    remainingSlots: 3,
    badgeText: 'Còn 3 slot',
  },
];

export const MOCK_AFTERNOON_SLOTS: BookingTimeSlot[] = [
  {
    id: 'slot-1330',
    timeRange: '13:30 - 14:00',
    shift: 'AFTERNOON',
    shiftLabel: 'Ca Chiều',
    status: 'AVAILABLE',
    remainingSlots: 4,
    badgeText: 'Còn 4 slot',
  },
  {
    id: 'slot-1400',
    timeRange: '14:00 - 14:30',
    shift: 'AFTERNOON',
    shiftLabel: 'Ca Chiều',
    status: 'AVAILABLE',
    remainingSlots: 3,
    badgeText: 'Còn 3 slot',
  },
  {
    id: 'slot-1430',
    timeRange: '14:30 - 15:00',
    shift: 'AFTERNOON',
    shiftLabel: 'Ca Chiều',
    status: 'AVAILABLE',
    remainingSlots: 5,
    badgeText: 'Còn 5 slot',
  },
  {
    id: 'slot-1500',
    timeRange: '15:00 - 15:30',
    shift: 'AFTERNOON',
    shiftLabel: 'Ca Chiều',
    status: 'AVAILABLE',
    remainingSlots: 4,
    badgeText: 'Còn 4 slot',
  },
  {
    id: 'slot-1530',
    timeRange: '15:30 - 16:00',
    shift: 'AFTERNOON',
    shiftLabel: 'Ca Chiều',
    status: 'AVAILABLE',
    remainingSlots: 2,
    badgeText: 'Còn 2 slot',
  },
  {
    id: 'slot-1600',
    timeRange: '16:00 - 16:30',
    shift: 'AFTERNOON',
    shiftLabel: 'Ca Chiều',
    status: 'LAST_SLOT',
    remainingSlots: 1,
    badgeText: 'Còn 1 slot cuối',
  },
];

export const DEFAULT_BOOKING_DOCTOR_SERVICE: BookingDoctorServiceInfo = {
  doctorId: 'doc-01',
  doctorName: 'BS.CKII Trần Quốc Tuấn',
  roleTag: 'Trưởng kíp khám - Khoa Tiêu Hóa - Gan Mật',
  specialty: 'Khoa Tiêu Hóa - Gan Mật',
  room: 'Phòng 204 - Tầng 2, Nhà A (Khu Khám Tự Nguyện)',
  shortRoom: 'Phòng 204',
  serviceId: 'srv-th-01',
  serviceCode: 'DV-TH-01',
  serviceName: 'Khám Chuyên khoa Tiêu Hóa',
  price: 250000,
  avatarUrl: '/assets/admin-doctors/list-01.png',
  availabilityLabel: 'Sẵn sàng',
  dutyNote: 'Lịch trực bác sĩ: Thứ Hai, Thứ Ba, Thứ Tư (Chiều), Thứ Năm (Sáng), Thứ Sáu, Thứ Bảy',
};

export function resolveDoctorDayShiftSchedule(dateIso?: string): DoctorDayShiftSchedule {
  if (dateIso === '2026-10-14') {
    return {
      hasMorningShift: false,
      hasAfternoonShift: true,
      shiftSummaryLabel: 'Ca trực Bác sĩ ngày 14/10: Chỉ trực Ca Chiều (13:30 - 16:30)',
      offShiftNote:
        'Buổi sáng bác sĩ có lịch mổ nội soi tiêu hóa tại khoa — chỉ mở đặt lịch Ca Chiều.',
      morningSlots: [],
      afternoonSlots: MOCK_AFTERNOON_SLOTS,
    };
  }

  if (dateIso === '2026-10-15' || dateIso === '2026-10-17') {
    return {
      hasMorningShift: true,
      hasAfternoonShift: false,
      shiftSummaryLabel: `Ca trực Bác sĩ ngày ${dateIso === '2026-10-15' ? '15/10' : '17/10'}: Chỉ trực Ca Sáng (07:30 - 11:30)`,
      offShiftNote:
        'Buổi chiều bác sĩ tham gia hội chẩn chuyên môn nội viện — chỉ mở đặt lịch Ca Sáng.',
      morningSlots: MOCK_MORNING_SLOTS,
      afternoonSlots: [],
    };
  }

  return {
    hasMorningShift: true,
    hasAfternoonShift: true,
    shiftSummaryLabel:
      'Ca trực Bác sĩ: Trực cả ngày — Ca Sáng (07:30 - 11:30) & Ca Chiều (13:30 - 16:30)',
    morningSlots: MOCK_MORNING_SLOTS,
    afternoonSlots: MOCK_AFTERNOON_SLOTS,
  };
}

export function resolveBookingDoctorService(
  doctorId?: string | null,
  serviceId?: string | null,
): BookingDoctorServiceInfo {
  const matchedDoctor = doctorId ? MOCK_DOCTORS.find((d) => d.id === doctorId) : undefined;
  const matchedService = serviceId
    ? MOCK_EXAMINATION_SERVICES.find(
        (s) => s.id === serviceId || s.serviceCode.toLowerCase() === serviceId.toLowerCase(),
      )
    : matchedDoctor
      ? MOCK_EXAMINATION_SERVICES.find((s) => s.departmentName === matchedDoctor.specialty)
      : undefined;

  if (!matchedDoctor && !matchedService) {
    return DEFAULT_BOOKING_DOCTOR_SERVICE;
  }

  const shortRoomMatch = matchedDoctor?.room.match(/Phòng\s+\d+/i);

  return {
    doctorId: matchedDoctor?.id ?? DEFAULT_BOOKING_DOCTOR_SERVICE.doctorId,
    doctorName: matchedDoctor?.name ?? DEFAULT_BOOKING_DOCTOR_SERVICE.doctorName,
    roleTag: matchedDoctor
      ? matchedDoctor.id === 'doc-01'
        ? DEFAULT_BOOKING_DOCTOR_SERVICE.roleTag
        : `${matchedDoctor.roleTag} - ${matchedDoctor.specialty}`
      : DEFAULT_BOOKING_DOCTOR_SERVICE.roleTag,
    specialty: matchedDoctor?.specialty ?? DEFAULT_BOOKING_DOCTOR_SERVICE.specialty,
    room:
      matchedDoctor?.id === 'doc-01'
        ? DEFAULT_BOOKING_DOCTOR_SERVICE.room
        : (matchedDoctor?.room ?? DEFAULT_BOOKING_DOCTOR_SERVICE.room),
    shortRoom: shortRoomMatch ? shortRoomMatch[0] : DEFAULT_BOOKING_DOCTOR_SERVICE.shortRoom,
    serviceId: matchedService?.id ?? DEFAULT_BOOKING_DOCTOR_SERVICE.serviceId,
    serviceCode: matchedService?.serviceCode ?? DEFAULT_BOOKING_DOCTOR_SERVICE.serviceCode,
    serviceName: matchedService?.name ?? DEFAULT_BOOKING_DOCTOR_SERVICE.serviceName,
    price: matchedDoctor?.price ?? matchedService?.price ?? DEFAULT_BOOKING_DOCTOR_SERVICE.price,
    avatarUrl: matchedDoctor?.avatarUrl ?? DEFAULT_BOOKING_DOCTOR_SERVICE.avatarUrl,
    availabilityLabel: 'Sẵn sàng',
    dutyNote: matchedDoctor
      ? `Ca trực gần nhất: ${matchedDoctor.nearestSchedule} (${matchedDoctor.scheduleStatusText})`
      : DEFAULT_BOOKING_DOCTOR_SERVICE.dutyNote,
  };
}

export function getMockAvailableSlotsPayload(
  doctorId?: string | null,
  serviceId?: string | null,
  dateIso?: string,
): AvailableSlotsPayload {
  const activeDayShift = resolveDoctorDayShiftSchedule(dateIso);
  return {
    doctorService: resolveBookingDoctorService(doctorId, serviceId),
    monthLabel: 'Tháng 10, 2026',
    profiles: MOCK_BOOKING_PROFILES,
    dates: MOCK_BOOKING_DATES,
    morningSlots: activeDayShift.morningSlots,
    afternoonSlots: activeDayShift.afternoonSlots,
    activeDayShift,
  };
}

export function resolveBookingServiceOnlyInfo(serviceId?: string | null): BookingServiceOnlyInfo {
  const matched = serviceId
    ? MOCK_EXAMINATION_SERVICES.find(
        (s) => s.id === serviceId || s.serviceCode.toLowerCase() === serviceId.toLowerCase(),
      )
    : undefined;

  const fallback = matched ?? MOCK_EXAMINATION_SERVICES[0];

  return {
    serviceId: fallback.id,
    serviceCode: fallback.serviceCode,
    serviceName: fallback.name,
    departmentName: fallback.departmentName,
    location: fallback.location ?? 'Khu Khám Ngoại trú - Tầng 2, Nhà A',
    durationMinutes: fallback.durationMinutes,
    durationDescription:
      fallback.durationDescription ?? `${fallback.durationMinutes} phút thăm khám trực tiếp`,
    price: fallback.price,
    onlineBadgeLabel: fallback.badgeLabel ?? 'Đặt online nhanh',
  };
}

export const MOCK_SERVICE_DATES: BookingDateOption[] = [
  {
    dateIso: '2026-10-12',
    dayTopLabel: 'Thứ 2',
    dayNumber: '12',
    subDateLabel: '12 Thg 10',
    fullDateDisplay: 'Thứ Hai, 12/10/2026',
    status: 'FULL',
    remainingSlots: 0,
    badgeText: 'Hết lượt',
  },
  {
    dateIso: '2026-10-13',
    dayTopLabel: 'Hôm nay',
    dayNumber: '13',
    subDateLabel: '13 Thg 10',
    fullDateDisplay: 'Thứ Ba, 13/10/2026',
    status: 'AVAILABLE',
    remainingSlots: 32,
    badgeText: '2 ca khả dụng',
    isToday: true,
  },
  {
    dateIso: '2026-10-14',
    dayTopLabel: 'Thứ 4',
    dayNumber: '14',
    subDateLabel: 'Ngày mai',
    fullDateDisplay: 'Thứ Tư, 14/10/2026',
    status: 'AVAILABLE',
    remainingSlots: 28,
    badgeText: '2 ca khả dụng',
  },
  {
    dateIso: '2026-10-15',
    dayTopLabel: 'Thứ 5',
    dayNumber: '15',
    subDateLabel: '15 Thg 10',
    fullDateDisplay: 'Thứ Năm, 15/10/2026',
    status: 'AVAILABLE',
    remainingSlots: 30,
    badgeText: '2 ca khả dụng',
  },
  {
    dateIso: '2026-10-16',
    dayTopLabel: 'Thứ 6',
    dayNumber: '16',
    subDateLabel: '16 Thg 10',
    fullDateDisplay: 'Thứ Sáu, 16/10/2026',
    status: 'AVAILABLE',
    remainingSlots: 24,
    badgeText: '2 ca khả dụng',
  },
  {
    dateIso: '2026-10-17',
    dayTopLabel: 'Thứ 7',
    dayNumber: '17',
    subDateLabel: '17 Thg 10',
    fullDateDisplay: 'Thứ Bảy, 17/10/2026',
    status: 'FEW_SLOTS',
    remainingSlots: 6,
    badgeText: 'Chỉ khám Ca Sáng',
  },
  {
    dateIso: '2026-10-18',
    dayTopLabel: 'Chủ Nhật',
    dayNumber: '18',
    subDateLabel: '18 Thg 10',
    fullDateDisplay: 'Chủ Nhật, 18/10/2026',
    status: 'OFF',
    remainingSlots: 0,
    badgeText: 'Nghỉ khám định kỳ',
  },
];

export function resolveServiceShiftsByDate(dateIso?: string): ServiceShiftOption[] {
  const isSaturday = dateIso === '2026-10-17';

  return [
    {
      id: 'shift-morning',
      shift: 'MORNING',
      title: 'Ca Khám Buổi Sáng',
      timeRange: '07:30 - 11:30',
      checkInWindow: 'Giờ tiếp đón tiêu chuẩn: 07:30 - 11:00',
      remainingCapacity: isSaturday ? 6 : 18,
      badgeText: isSaturday ? 'Còn 6 lượt khám' : 'Còn 18 lượt khám',
      status: isSaturday ? 'FEW_SLOTS' : 'AVAILABLE',
      description:
        'Phù hợp khi cần thực hiện xét nghiệm máu nhịn ăn sáng, siêu âm ổ bụng hoặc nội soi tiêu hóa trong ngày.',
    },
    {
      id: 'shift-afternoon',
      shift: 'AFTERNOON',
      title: 'Ca Khám Buổi Chiều',
      timeRange: '13:30 - 16:30',
      checkInWindow: 'Giờ tiếp đón tiêu chuẩn: 13:30 - 16:00',
      remainingCapacity: isSaturday ? 0 : 14,
      badgeText: isSaturday ? 'Nghỉ chiều Thứ 7' : 'Còn 14 lượt khám',
      status: isSaturday ? 'FULL' : 'AVAILABLE',
      description:
        'Khám chuyên khoa ngoại trú, tư vấn kết quả cận lâm sàng và kê đơn thuốc điều trị ngoại trú thông thoáng.',
    },
  ];
}

export function getMockServiceAvailableShiftsPayload(
  serviceId?: string | null,
  dateIso?: string,
): ServiceAvailableShiftsPayload {
  return {
    serviceInfo: resolveBookingServiceOnlyInfo(serviceId),
    monthLabel: 'Tháng 10, 2026',
    profiles: MOCK_BOOKING_PROFILES,
    dates: MOCK_SERVICE_DATES,
    shifts: resolveServiceShiftsByDate(dateIso),
  };
}
