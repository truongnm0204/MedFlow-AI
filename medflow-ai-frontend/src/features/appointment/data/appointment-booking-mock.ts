import type { AppointmentListItem } from '../types/appointment-list.types';
import type { CreateAppointmentRequest } from '../types/booking-review.types';
import {
  resolveDoctorBookingSummary,
  resolveServiceBookingSummary,
} from '../utils/booking-review-summary';
import { MOCK_APPOINTMENT_MEMBERS, MOCK_APPOINTMENTS } from './appointment-list-mock';
import { MOCK_DOCTORS } from './doctor-search-mock';
import {
  getMockAvailableSlotsPayload,
  getMockServiceAvailableShiftsPayload,
} from './slot-booking-mock';

const PROFILE_TO_MEMBER_ID: Record<string, string> = {
  'pat-self': 'member-self',
  'pat-child': 'member-child',
  'pat-mother': 'member-mother',
};

function buildUniqueAppointmentCode(dateIso: string, timeRange: string): string {
  const [yyyy = '2026', mm = '01', dd = '01'] = dateIso.split('-');
  const startTime = timeRange.split(' - ')[0] ?? '08:00';
  const [hh = '08', mi = '00'] = startTime.trim().split(':');
  const baseCode = `APT-${yyyy}-${mm}${dd}-${hh}${mi}`;
  const existingCodes = new Set(
    MOCK_APPOINTMENTS.map((item) => item.appointmentCode.toLowerCase()),
  );

  if (!existingCodes.has(baseCode.toLowerCase())) {
    return baseCode;
  }

  let suffix = 2;
  while (existingCodes.has(`${baseCode}-${suffix}`.toLowerCase())) {
    suffix += 1;
  }
  return `${baseCode}-${suffix}`;
}

export function createMockAppointment(request: CreateAppointmentRequest): AppointmentListItem {
  const summary =
    request.mode === 'doctor'
      ? resolveDoctorBookingSummary(
          request,
          getMockAvailableSlotsPayload(request.doctorId, request.serviceId, request.dateIso),
        )
      : resolveServiceBookingSummary(
          request,
          getMockServiceAvailableShiftsPayload(request.serviceId, request.dateIso),
        );

  if (!summary) {
    throw new Error('Khung giờ đã chọn không còn trống. Vui lòng quay lại chọn khung giờ khác.');
  }

  const appointmentCode = buildUniqueAppointmentCode(request.dateIso, summary.timeRange);
  const id = appointmentCode.toLowerCase();
  const queueNumber = String(
    MOCK_APPOINTMENTS.filter((item) => item.scheduledDate === request.dateIso).length + 12,
  ).padStart(3, '0');

  const profile = summary.patient;
  const patientId = PROFILE_TO_MEMBER_ID[profile.id] ?? profile.id;
  const matchedMember = MOCK_APPOINTMENT_MEMBERS.find((member) => member.id === patientId);
  const patientCode = matchedMember?.patientCode ?? profile.patientCode ?? '';
  const patientRelationshipLabel = profile.relationshipLabel.replace(/[()]/g, '');
  const patientAgeLabel = `${profile.age} tuổi`;
  const patientBirthDateLabel = profile.birthDateLabel
    ? `${profile.birthDateLabel} (${profile.age} tuổi) • ${profile.gender}`
    : undefined;

  const matchedDoctor =
    request.mode === 'doctor'
      ? MOCK_DOCTORS.find((doctor) => doctor.id === request.doctorId)
      : undefined;

  const roomMatch = summary.roomLabel.match(/Phòng\s+(\d+)/i);
  const roomCode = roomMatch?.[1] ? `P.${roomMatch[1]}` : summary.roomLabel;
  const roomName = summary.roomLabel.split(' (')[0] ?? summary.roomLabel;
  const zoneMatch = summary.roomLabel.match(/\(([^)]+)\)\s*$/);
  const roomZoneLabel = zoneMatch?.[1] ?? 'Khu Khám Ngoại Trú Tự Nguyện';

  const [yyyy = '2026', mm = '01', dd = '01'] = request.dateIso.split('-');
  const formattedDate = `${dd}/${mm}/${yyyy}`;
  const scheduledDateBadge = summary.date.isToday ? `Hôm nay, ${formattedDate}` : formattedDate;

  const item: AppointmentListItem = {
    id,
    appointmentCode,
    patientId,
    patientCode,
    patientName: profile.fullName,
    patientRelationshipLabel,
    patientAgeLabel,
    patientBirthDateLabel,
    patientPhone: profile.phone,
    patientNationalId: profile.nationalId,
    patientAddress: profile.address,
    serviceId: summary.serviceId,
    serviceCode: summary.serviceCode,
    serviceName: summary.serviceName,
    serviceSubLabel: 'Khám theo giờ yêu cầu tự nguyện',
    listedFeeServiceLabel: summary.serviceName,
    specialtyId: request.mode === 'doctor' ? (matchedDoctor?.specialtyId ?? '') : '',
    specialtyName: summary.specialtyName,
    departmentName: summary.specialtyName,
    doctorId: request.mode === 'doctor' ? (request.doctorId ?? '') : '',
    doctorCode: '',
    doctorName:
      request.mode === 'doctor' ? (summary.doctorName ?? '') : 'Bác sĩ phân bổ khi check-in',
    doctorTitle: request.mode === 'doctor' ? (matchedDoctor?.title ?? '') : '',
    doctorRoleLabel:
      request.mode === 'doctor' ? (summary.doctorRoleTag ?? '') : 'Phân luồng tự động theo ca khám',
    roomCode,
    roomName,
    roomZoneLabel,
    scheduledDate: request.dateIso,
    scheduledDateBadge,
    scheduledDateFullLabel: `${summary.date.fullDateDisplay} (${summary.shiftLabel})`,
    scheduledTimeRange: summary.timeRange,
    checkInDeadlineTime: summary.checkInDeadlineTime,
    checkInCounterLabel: 'Quầy Tiếp đón Tầng 1 - Nhà A',
    queueNumber,
    bookingChannel: 'ONLINE',
    bookingTypeLabel:
      request.mode === 'doctor'
        ? 'Khám chuyên khoa hẹn giờ tự nguyện'
        : 'Khám theo ca dịch vụ tự nguyện',
    status: 'CONFIRMED',
    statusBannerLabel: 'ĐÃ XÁC NHẬN - SẮP ĐẾN GIỜ KHÁM',
    listedFee: summary.price,
    feePolicyNote: 'Khám Tự Nguyện trọn gói • Không áp dụng BHYT',
    paymentStatus: 'UNPAID_AT_COUNTER',
    paymentStatusLabel: 'Chờ thanh toán tại quầy',
    paymentSubNote: 'Hỗ trợ tiền mặt, quẹt POS hoặc VietQR động tại quầy thủ tục',
    reasonForVisit: request.reasonForVisit.trim() || null,
    bookedAt: new Date().toISOString(),
  };

  MOCK_APPOINTMENTS.unshift(item);
  return { ...item };
}
