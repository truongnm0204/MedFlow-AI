import { apiClient } from '@/shared/lib/axios';

import type {
  AppointmentListItem,
  ProposedRescheduleSchedulePayload,
  ProposedRescheduleSlotOption,
} from '../types/appointment-list.types';
import type {
  AvailableSlotsPayload,
  BookingDateOption,
  BookingTimeSlot,
  ServiceAvailableShiftsPayload,
} from '../types/slot-booking.types';

export async function fetchAppointmentDetailByIdApi(
  appointmentId: string,
): Promise<AppointmentListItem> {
  const cleanId = appointmentId.trim();
  if (!cleanId) {
    throw new Error('Mã lịch hẹn không hợp lệ.');
  }

  const { data } = await apiClient.get<AppointmentListItem>(
    `/appointments/${encodeURIComponent(cleanId)}`,
  );
  return data;
}

interface RawScheduleResponse {
  monthLabel?: string;
  dates?: BookingDateOption[];
  morningSlots?: BookingTimeSlot[];
  afternoonSlots?: BookingTimeSlot[];
}

export async function fetchRescheduleScheduleApi(
  appointment: AppointmentListItem,
  requestedDateIso?: string,
): Promise<ProposedRescheduleSchedulePayload> {
  const trimmedDoctorId = appointment.doctorId?.trim() ?? '';
  const trimmedServiceId = appointment.serviceId?.trim() ?? '';
  const trimmedDateIso = requestedDateIso?.trim() || undefined;
  const isDoctorMode = Boolean(trimmedDoctorId);

  let rawData: RawScheduleResponse;
  if (isDoctorMode) {
    const { data } = await apiClient.get<AvailableSlotsPayload>('/appointments/available-slots', {
      params: {
        doctorId: trimmedDoctorId,
        serviceId: trimmedServiceId || undefined,
        ...(trimmedDateIso ? { dateIso: trimmedDateIso } : {}),
      },
    });
    rawData = data;
  } else {
    const { data } = await apiClient.get<ServiceAvailableShiftsPayload>(
      '/appointments/service-shifts',
      {
        params: {
          serviceId: trimmedServiceId || undefined,
          ...(trimmedDateIso ? { dateIso: trimmedDateIso } : {}),
        },
      },
    );
    rawData = data;
  }

  const dates = rawData.dates ?? [];
  const activeDate =
    (trimmedDateIso
      ? dates.find((d) => d.dateIso === trimmedDateIso && d.status !== 'OFF' && d.status !== 'FULL')
      : undefined) ??
    dates.find((d) => d.status !== 'OFF' && d.status !== 'FULL') ??
    (trimmedDateIso ? dates.find((d) => d.dateIso === trimmedDateIso) : undefined) ??
    dates[0];

  const activeDateIso = activeDate?.dateIso ?? trimmedDateIso ?? appointment.scheduledDate;

  const allOpen = [...(rawData.morningSlots ?? []), ...(rawData.afternoonSlots ?? [])].filter(
    (s) =>
      s.status !== 'FULL' &&
      Boolean(s.id?.trim()) &&
      !(
        activeDateIso === appointment.scheduledDate &&
        s.timeRange === appointment.scheduledTimeRange
      ),
  );

  const doctorDisplay = isDoctorMode
    ? appointment.departmentName
      ? `${appointment.doctorName} (${appointment.departmentName})`
      : appointment.doctorName
    : appointment.departmentName
      ? `Bác sĩ điều phối theo ca khám (${appointment.departmentName})`
      : 'Bác sĩ điều phối theo ca khám';

  const slots: ProposedRescheduleSlotOption[] = allOpen.map((slot) => {
    const startPart = slot.timeRange.split('-')[0]?.trim() ?? '08:00';
    const remainingLabel =
      slot.badgeText?.trim() ||
      (typeof slot.remainingSlots === 'number' ? `Còn ${slot.remainingSlots} slot` : 'Trống chỗ');

    return {
      slotId: slot.id,
      scheduledDate: activeDateIso,
      scheduledDateBadge: activeDate
        ? `${activeDate.dayTopLabel}, ${activeDate.subDateLabel}`
        : appointment.scheduledDateBadge,
      scheduledDateFullLabel:
        activeDate?.fullDateDisplay ??
        `${appointment.scheduledDateBadge} (${slot.shift === 'AFTERNOON' ? 'Ca Chiều' : 'Ca Sáng'})`,
      scheduledTimeRange: slot.timeRange,
      checkInDeadlineTime: startPart,
      estimatedQueueNumber: null,
      remainingLabel,
      roomCode: appointment.roomCode || 'Theo điều phối',
      roomName: appointment.roomName || 'Phòng khám theo điều phối',
      doctorName: doctorDisplay,
    };
  });

  return {
    dates,
    activeDateIso,
    monthLabel: rawData.monthLabel,
    slots,
  };
}

export async function fetchProposedRescheduleSlotsApi(
  appointment: AppointmentListItem,
  requestedDateIso?: string,
): Promise<ProposedRescheduleSlotOption[]> {
  const schedule = await fetchRescheduleScheduleApi(appointment, requestedDateIso);
  return schedule.slots;
}

export interface RescheduleAppointmentPayload {
  appointmentId: string;
  targetSlotId: string;
  rescheduleNote?: string;
}

export async function rescheduleAppointmentApi({
  appointmentId,
  targetSlotId,
  rescheduleNote,
}: RescheduleAppointmentPayload): Promise<AppointmentListItem> {
  const { data } = await apiClient.post<AppointmentListItem>(
    `/appointments/${encodeURIComponent(appointmentId)}/reschedule`,
    { targetSlotId, rescheduleNote },
  );
  return data;
}

export interface CancelAppointmentPayload {
  appointmentId: string;
  cancellationReason: string;
}

export async function cancelAppointmentApi({
  appointmentId,
  cancellationReason,
}: CancelAppointmentPayload): Promise<AppointmentListItem> {
  const { data } = await apiClient.post<AppointmentListItem>(
    `/appointments/${encodeURIComponent(appointmentId)}/cancel`,
    { cancellationReason },
  );
  return data;
}

export interface UpdateAppointmentReasonPayload {
  appointmentId: string;
  reasonForVisit: string;
}

export async function updateAppointmentReasonApi({
  appointmentId,
  reasonForVisit,
}: UpdateAppointmentReasonPayload): Promise<AppointmentListItem> {
  const { data } = await apiClient.patch<AppointmentListItem>(
    `/appointments/${encodeURIComponent(appointmentId)}/reason`,
    { reasonForVisit },
  );
  return data;
}
