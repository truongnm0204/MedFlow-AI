import type { BookingReviewSummary, CreateAppointmentRequest } from '../types/booking-review.types';
import type {
  AvailableSlotsPayload,
  ServiceAvailableShiftsPayload,
} from '../types/slot-booking.types';

export function parseBookingReviewRequest(
  searchParams: URLSearchParams,
  reasonForVisit: string,
): CreateAppointmentRequest | null {
  const mode = searchParams.get('mode');
  if (mode !== 'doctor' && mode !== 'service') {
    return null;
  }

  const patientId = searchParams.get('patientId')?.trim() ?? '';
  const dateIso = searchParams.get('date')?.trim() ?? '';
  if (!patientId || !dateIso) {
    return null;
  }

  const doctorId = searchParams.get('doctorId')?.trim() || null;
  const serviceId = searchParams.get('serviceId')?.trim() || null;
  const slotId = searchParams.get('slotId')?.trim() || null;
  const shiftId = searchParams.get('shiftId')?.trim() || null;

  if (mode === 'doctor' && (!doctorId || !slotId)) {
    return null;
  }

  if (mode === 'service' && (!serviceId || !shiftId)) {
    return null;
  }

  return {
    mode,
    doctorId,
    serviceId,
    patientId,
    dateIso,
    slotId,
    shiftId,
    reasonForVisit,
  };
}

export function subtractMinutes(time: string, minutes: number): string {
  const [hoursPart, minutesPart] = time.trim().split(':');
  const hours = Number(hoursPart);
  const mins = Number(minutesPart);
  if (Number.isNaN(hours) || Number.isNaN(mins)) {
    return time;
  }

  const totalMinutes = (hours * 60 + mins - minutes + 24 * 60) % (24 * 60);
  const resultHours = Math.floor(totalMinutes / 60);
  const resultMinutes = totalMinutes % 60;
  return `${String(resultHours).padStart(2, '0')}:${String(resultMinutes).padStart(2, '0')}`;
}

export function resolveDoctorBookingSummary(
  request: CreateAppointmentRequest,
  payload: AvailableSlotsPayload,
): BookingReviewSummary | null {
  const patient = payload.profiles.find((p) => p.id === request.patientId);
  const date = payload.dates.find(
    (d) => d.dateIso === request.dateIso && d.status !== 'FULL' && d.status !== 'OFF',
  );
  const slot = [...payload.morningSlots, ...payload.afternoonSlots].find(
    (s) => s.id === request.slotId && s.status !== 'FULL',
  );

  if (!patient || !date || !slot) {
    return null;
  }

  const { doctorService } = payload;
  const startTime = slot.timeRange.split(' - ')[0] ?? '';

  return {
    mode: 'doctor',
    patient,
    date,
    timeRange: slot.timeRange,
    shiftLabel: slot.shiftLabel,
    scheduleLabel: `${slot.timeRange}, ${date.fullDateDisplay} (${slot.shiftLabel})`,
    checkInDeadlineTime: subtractMinutes(startTime, 15),
    doctorName: doctorService.doctorName,
    doctorRoleTag: doctorService.roleTag,
    doctorAvatarUrl: doctorService.avatarUrl,
    specialtyName: doctorService.specialty,
    serviceId: doctorService.serviceId,
    serviceCode: doctorService.serviceCode,
    serviceName: doctorService.serviceName,
    roomLabel: doctorService.room,
    durationMinutes: null,
    durationDescription: null,
    price: doctorService.price,
  };
}

export function resolveServiceBookingSummary(
  request: CreateAppointmentRequest,
  payload: ServiceAvailableShiftsPayload,
): BookingReviewSummary | null {
  const patient = payload.profiles.find((p) => p.id === request.patientId);
  const date = payload.dates.find(
    (d) => d.dateIso === request.dateIso && d.status !== 'FULL' && d.status !== 'OFF',
  );
  const shift = payload.shifts.find((s) => s.id === request.shiftId && s.status !== 'FULL');

  if (!patient || !date || !shift) {
    return null;
  }

  const shiftLabel = shift.shift === 'MORNING' ? 'Ca Sáng' : 'Ca Chiều';
  const startTime = shift.timeRange.split(' - ')[0] ?? '';
  const { serviceInfo } = payload;

  return {
    mode: 'service',
    patient,
    date,
    timeRange: shift.timeRange,
    shiftLabel,
    scheduleLabel: `${shift.timeRange}, ${date.fullDateDisplay} (${shiftLabel})`,
    checkInDeadlineTime: subtractMinutes(startTime, 15),
    doctorName: null,
    doctorRoleTag: null,
    doctorAvatarUrl: null,
    specialtyName: serviceInfo.departmentName,
    serviceId: serviceInfo.serviceId,
    serviceCode: serviceInfo.serviceCode,
    serviceName: serviceInfo.serviceName,
    roomLabel: serviceInfo.location,
    durationMinutes: serviceInfo.durationMinutes,
    durationDescription: serviceInfo.durationDescription,
    price: serviceInfo.price,
  };
}
