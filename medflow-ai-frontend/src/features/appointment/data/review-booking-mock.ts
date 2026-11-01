import { MOCK_APPOINTMENTS } from './appointment-list-mock';
import { MOCK_DOCTORS } from './doctor-search-mock';
import { MOCK_EXAMINATION_SERVICES } from './service-search-mock';
import {
  MOCK_AFTERNOON_SLOTS,
  MOCK_BOOKING_DATES,
  MOCK_MORNING_SLOTS,
  MOCK_SERVICE_DATES,
  resolveBookingDoctorService,
  resolveBookingServiceOnlyInfo,
  resolveServiceShiftsByDate,
} from './slot-booking-mock';
import type {
  AppointmentReviewPayload,
  ConfirmBookingRequest,
  ConfirmBookingResponse,
  ReviewBookingMode,
  ReviewBookingQueryParams,
  ReviewPatientEhrInfo,
} from '../types/review-booking.types';

export const DEFAULT_REVIEW_SYMPTOM_NOTE =
  'Đau tức vùng thượng vị, ợ chua kéo dài, tái khám theo dõi dạ dày trào ngược GERD.';

const MOCK_PATIENT_EHR_MAP: Record<string, Omit<ReviewPatientEhrInfo, 'symptomNote'>> = {
  'pat-self': {
    patientId: 'pat-self',
    patientCode: 'PAT-8921',
    fullName: 'Nguyễn Văn An',
    relationshipBadgeLabel: 'Chính chủ tài khoản',
    isPrimaryAccount: true,
    dateOfBirth: '15/08/1986',
    ageLabel: '(40 tuổi)',
    gender: 'Nam',
    phone: '0912 345 678',
    nationalId: '001086001234',
    currentAddress: 'Phường Hàng Trống, Quận Hoàn Kiếm, TP. Hà Nội',
  },
  'pat-child': {
    patientId: 'pat-child',
    patientCode: 'PAT-9012',
    fullName: 'Nguyễn Bảo Châu',
    relationshipBadgeLabel: 'Hồ sơ người thân • Con gái',
    isPrimaryAccount: false,
    dateOfBirth: '20/05/2018',
    ageLabel: '(8 tuổi)',
    gender: 'Nữ',
    phone: '0912 345 678',
    nationalId: '001318004512',
    currentAddress: 'Phường Hàng Trống, Quận Hoàn Kiếm, TP. Hà Nội',
  },
  'pat-mother': {
    patientId: 'pat-mother',
    patientCode: 'PAT-4481',
    fullName: 'Lê Thị Mai',
    relationshipBadgeLabel: 'Hồ sơ người thân • Mẹ ruột',
    isPrimaryAccount: false,
    dateOfBirth: '10/11/1958',
    ageLabel: '(68 tuổi)',
    gender: 'Nữ',
    phone: '0912 345 678',
    nationalId: '001158009871',
    currentAddress: 'Phường Hàng Trống, Quận Hoàn Kiếm, TP. Hà Nội',
  },
};

export function computeCheckInDeadline(timeRange: string, earlyMinutes = 15): string {
  const match = timeRange.match(/(\d{2}):(\d{2})/);
  if (!match) return '08:45';

  const hours = Number.parseInt(match[1], 10);
  const minutes = Number.parseInt(match[2], 10);
  const totalMinutes = Math.max(0, hours * 60 + minutes - earlyMinutes);
  const outHours = String(Math.floor(totalMinutes / 60)).padStart(2, '0');
  const outMinutes = String(totalMinutes % 60).padStart(2, '0');
  return `${outHours}:${outMinutes}`;
}

function formatFullDateWithNgay(fullDateDisplay: string): string {
  // e.g. 'Thứ Ba, 13/10/2026' -> 'Thứ Ba, ngày 13/10/2026'
  if (fullDateDisplay.includes('ngày ')) {
    return fullDateDisplay;
  }
  const parts = fullDateDisplay.split(', ');
  if (parts.length === 2) {
    return `${parts[0]}, ngày ${parts[1]}`;
  }
  return fullDateDisplay;
}

function extractRoomNumber(roomTitle: string): string {
  const match = roomTitle.match(/Phòng\s+(\d+)/i);
  return match ? match[1] : '204';
}

export function resolveReviewPatientEhr(
  patientId?: string | null,
  symptomNote?: string | null,
): ReviewPatientEhrInfo {
  const normalizedId = patientId?.trim() || 'pat-self';
  const baseProfile = MOCK_PATIENT_EHR_MAP[normalizedId] ?? MOCK_PATIENT_EHR_MAP['pat-self'];
  const resolvedNote = symptomNote?.trim() ? symptomNote.trim() : DEFAULT_REVIEW_SYMPTOM_NOTE;

  return {
    ...baseProfile,
    symptomNote: resolvedNote,
  };
}

export function getMockAppointmentReviewPayload(
  params: ReviewBookingQueryParams = {},
): AppointmentReviewPayload {
  const mode: ReviewBookingMode = params.mode === 'service' ? 'service' : 'doctor';
  const patientInfo = resolveReviewPatientEhr(params.patientId, params.symptomNote);
  const dateIso = params.dateIso?.trim() || '2026-10-13';

  if (mode === 'service') {
    const serviceOnly = resolveBookingServiceOnlyInfo(params.serviceId ?? params.serviceCode);
    const serviceDate =
      MOCK_SERVICE_DATES.find((d) => d.dateIso === dateIso) ?? MOCK_SERVICE_DATES[1];
    const shifts = resolveServiceShiftsByDate(serviceDate.dateIso);
    const selectedShift =
      shifts.find((s) => s.id === params.shiftId) ??
      shifts.find((s) => s.status !== 'FULL') ??
      shifts[0];

    const matchedDoctor =
      MOCK_DOCTORS.find((d) => d.specialty === serviceOnly.departmentName) ?? MOCK_DOCTORS[0];
    const roomTitle =
      matchedDoctor.id === 'doc-01'
        ? 'Phòng 204 - Tầng 2, Nhà A (Khu Khám Ngoại Trú Tự Nguyện)'
        : matchedDoctor.room;
    const shortRoomNumber = extractRoomNumber(roomTitle);
    const checkInDeadlineTime = computeCheckInDeadline(selectedShift.timeRange, 15);
    const formattedDate = formatFullDateWithNgay(serviceDate.fullDateDisplay);
    const shiftShortLabel = selectedShift.shift === 'MORNING' ? 'Ca Sáng' : 'Ca Chiều';

    return {
      patientInfo,
      scheduleLocation: {
        mode: 'service',
        doctorId: matchedDoctor.id,
        doctorName: matchedDoctor.name,
        doctorRoleBadge: 'BÁC SĨ PHỤ TRÁCH CA TRỰC CHUYÊN KHOA',
        doctorCredentialsSubtitle: `${matchedDoctor.yearsOfExperience} năm kinh nghiệm • Điều phối tự động theo ca khám`,
        doctorAvatarUrl: matchedDoctor.avatarUrl,
        specialtyName: serviceOnly.departmentName,
        serviceId: serviceOnly.serviceId,
        serviceCode: serviceOnly.serviceCode,
        serviceName: serviceOnly.serviceName,
        serviceDisplayLabel: `${serviceOnly.serviceName} (Mã ${serviceOnly.serviceCode})`,
        dateIso: serviceDate.dateIso,
        slotOrShiftId: selectedShift.id,
        scheduledSlotDisplay: `${selectedShift.timeRange}, ${formattedDate} (${shiftShortLabel})`,
        checkInDeadlineTime,
        allocatedRoomTitle: roomTitle,
        shortRoomNumber,
        durationBadgeLabel: `${serviceOnly.durationMinutes} phút/lượt khám`,
        durationFootnote: `Thời lượng chuẩn: ${serviceOnly.durationMinutes} phút thăm khám chuyên sâu trực tiếp kèm tư vấn phác đồ điều trị.`,
      },
      feeSummary: {
        serviceTypeBadge: 'Dịch vụ Ngoại trú Tự nguyện',
        items: [
          {
            id: 'fee-main-consultation',
            title: `${serviceOnly.serviceName} (${matchedDoctor.title})`,
            description: 'Khám lâm sàng chuyên sâu, chẩn đoán ban đầu & tư vấn',
            amount: serviceOnly.price,
          },
          {
            id: 'fee-vitals-ehr',
            title: 'Đo sinh hiệu ban đầu & Lập hồ sơ y tế số',
            description: 'Đo huyết áp, nhịp tim, SpO2, lập mã QR định danh hồ sơ',
            amount: 0,
            isIncludedFree: true,
            freeBadgeLabel: '0 VNĐ (Đã bao gồm trọn gói)',
          },
        ],
        totalAmount: serviceOnly.price,
        totalDisclaimer:
          'Chưa bao gồm xét nghiệm hoặc chẩn đoán hình ảnh chuyên sâu (nếu có chỉ định từ bác sĩ)',
        paymentMethodNote:
          'Quý khách có thể quét mã VietQR động bảo mật ngay sau khi xác nhận trên hệ thống hoặc thanh toán trực tiếp bằng tiền mặt / quẹt thẻ POS tại Quầy Tiếp đón khi đến viện.',
      },
      receptionPolicy: {
        badgeLabel: 'Chính xác theo phút',
        roomNumber: shortRoomNumber,
        receptionCounterLabel: 'Quầy Tiếp đón Ngoại trú (Tầng 1 - Nhà A)',
        earlyArrivalMinutes: 15,
        checkInDeadlineTime,
        lateGraceMinutes: 15,
      },
    };
  }

  // Doctor mode (default — Figma 14:1717)
  const doctorService = resolveBookingDoctorService(
    params.doctorId,
    params.serviceId ?? params.serviceCode,
  );
  const matchedDoctor =
    MOCK_DOCTORS.find((d) => d.id === doctorService.doctorId) ?? MOCK_DOCTORS[0];
  const matchedService =
    MOCK_EXAMINATION_SERVICES.find(
      (s) =>
        s.id === doctorService.serviceId ||
        s.serviceCode.toLowerCase() === doctorService.serviceCode.toLowerCase(),
    ) ?? MOCK_EXAMINATION_SERVICES[0];

  const bookingDate =
    MOCK_BOOKING_DATES.find((d) => d.dateIso === dateIso) ?? MOCK_BOOKING_DATES[1];
  const allSlots = [...MOCK_MORNING_SLOTS, ...MOCK_AFTERNOON_SLOTS];
  const selectedSlot =
    allSlots.find((s) => s.id === params.slotId) ??
    allSlots.find((s) => s.id === 'slot-0900') ??
    allSlots[3];

  const isDefaultTuanDoctor = doctorService.doctorId === 'doc-01';
  const allocatedRoomTitle = isDefaultTuanDoctor
    ? 'Phòng 204 - Tầng 2, Nhà A (Khu Khám Ngoại Trú Tự Nguyện)'
    : doctorService.room;
  const shortRoomNumber = extractRoomNumber(allocatedRoomTitle);
  const checkInDeadlineTime = computeCheckInDeadline(selectedSlot.timeRange, 15);
  const formattedDate = formatFullDateWithNgay(bookingDate.fullDateDisplay);
  const durationMinutes = matchedService.durationMinutes || 25;

  const consultationFeeTitle = isDefaultTuanDoctor
    ? 'Phí khám chuyên khoa Tiêu Hóa (BS.CKII)'
    : `Phí ${doctorService.serviceName} (${matchedDoctor.title})`;

  return {
    patientInfo,
    scheduleLocation: {
      mode: 'doctor',
      doctorId: doctorService.doctorId,
      doctorName: doctorService.doctorName,
      doctorRoleBadge: isDefaultTuanDoctor
        ? 'TRƯỞNG KÍP KHÁM - BÁC SĨ ĐIỀU TRỊ CAO CẤP'
        : `${matchedDoctor.roleTag.toUpperCase()} - BÁC SĨ ĐIỀU TRỊ CHUYÊN KHOA`,
      doctorCredentialsSubtitle: isDefaultTuanDoctor
        ? 'Chuyên gia Tiêu Hóa - Gan Mật • 18 năm kinh nghiệm nội trú'
        : `${matchedDoctor.specialty.replace(/^Khoa\s+/i, 'Chuyên gia ')} • ${matchedDoctor.yearsOfExperience} năm kinh nghiệm`,
      doctorAvatarUrl: doctorService.avatarUrl,
      specialtyName: doctorService.specialty,
      serviceId: doctorService.serviceId,
      serviceCode: doctorService.serviceCode,
      serviceName: doctorService.serviceName,
      serviceDisplayLabel: `${doctorService.serviceName} (Mã ${doctorService.serviceCode})`,
      dateIso: bookingDate.dateIso,
      slotOrShiftId: selectedSlot.id,
      scheduledSlotDisplay: `${selectedSlot.timeRange}, ${formattedDate} (${selectedSlot.shiftLabel})`,
      checkInDeadlineTime,
      allocatedRoomTitle,
      shortRoomNumber,
      durationBadgeLabel: `${durationMinutes} phút/lượt khám`,
      durationFootnote: isDefaultTuanDoctor
        ? 'Thời lượng chuẩn: 25 phút thăm khám chuyên sâu trực tiếp kèm tư vấn phác đồ dinh dưỡng tiêu hóa.'
        : `Thời lượng chuẩn: ${durationMinutes} phút thăm khám chuyên sâu trực tiếp kèm tư vấn phác đồ điều trị chuyên khoa.`,
    },
    feeSummary: {
      serviceTypeBadge: 'Dịch vụ Ngoại trú Tự nguyện',
      items: [
        {
          id: 'fee-main-consultation',
          title: consultationFeeTitle,
          description: 'Khám lâm sàng chuyên sâu, chẩn đoán ban đầu & tư vấn',
          amount: doctorService.price,
        },
        {
          id: 'fee-vitals-ehr',
          title: 'Đo sinh hiệu ban đầu & Lập hồ sơ y tế số',
          description: 'Đo huyết áp, nhịp tim, SpO2, lập mã QR định danh hồ sơ',
          amount: 0,
          isIncludedFree: true,
          freeBadgeLabel: '0 VNĐ (Đã bao gồm trọn gói)',
        },
      ],
      totalAmount: doctorService.price,
      totalDisclaimer:
        'Chưa bao gồm xét nghiệm hoặc chẩn đoán hình ảnh chuyên sâu (nếu có chỉ định từ bác sĩ)',
      paymentMethodNote:
        'Quý khách có thể quét mã VietQR động bảo mật ngay sau khi xác nhận trên hệ thống hoặc thanh toán trực tiếp bằng tiền mặt / quẹt thẻ POS tại Quầy Tiếp đón khi đến viện.',
    },
    receptionPolicy: {
      badgeLabel: 'Chính xác theo phút',
      roomNumber: shortRoomNumber,
      receptionCounterLabel: 'Quầy Tiếp đón Ngoại trú (Tầng 1 - Nhà A)',
      earlyArrivalMinutes: 15,
      checkInDeadlineTime,
      lateGraceMinutes: 15,
    },
  };
}

export function buildMockConfirmBookingResponse(
  request: ConfirmBookingRequest,
): ConfirmBookingResponse {
  const review = getMockAppointmentReviewPayload({
    mode: request.mode,
    doctorId: request.doctorId,
    serviceId: request.serviceId,
    serviceCode: request.serviceCode,
    patientId: request.patientId,
    dateIso: request.dateIso,
    slotId: request.mode === 'doctor' ? request.slotOrShiftId : undefined,
    shiftId: request.mode === 'service' ? request.slotOrShiftId : undefined,
    symptomNote: request.symptomNote,
  });

  // Synchronize MOCK_APPOINTMENTS[0] so navigating to /appointments/apt-1013-0902 reflects the confirmed booking
  const targetAppointment = MOCK_APPOINTMENTS[0];
  if (targetAppointment) {
    targetAppointment.patientCode = review.patientInfo.patientCode;
    targetAppointment.patientName = review.patientInfo.fullName;
    targetAppointment.patientBirthDateLabel = `${review.patientInfo.dateOfBirth} ${review.patientInfo.ageLabel} • ${review.patientInfo.gender}`;
    targetAppointment.patientPhone = review.patientInfo.phone;
    targetAppointment.patientNationalId = review.patientInfo.nationalId;
    targetAppointment.patientAddress = review.patientInfo.currentAddress;
    targetAppointment.doctorName = review.scheduleLocation.doctorName;
    targetAppointment.departmentName = review.scheduleLocation.specialtyName;
    targetAppointment.serviceCode = review.scheduleLocation.serviceCode;
    targetAppointment.serviceName = review.scheduleLocation.serviceName;
    targetAppointment.scheduledDate = review.scheduleLocation.dateIso;
    targetAppointment.checkInDeadlineTime = review.scheduleLocation.checkInDeadlineTime;
    targetAppointment.listedFee = review.feeSummary.totalAmount;
    targetAppointment.reasonForVisit = review.patientInfo.symptomNote;
    targetAppointment.status = 'CONFIRMED';
  }

  return {
    appointmentId: 'apt-1013-0902',
    appointmentCode: 'APT-2026-1013-0902',
    queueNumber: '012',
    status: 'CONFIRMED',
  };
}
