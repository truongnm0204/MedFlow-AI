import { apiClient } from '@/shared/lib/axios';

import type {
  AppointmentListItem,
  ProposedRescheduleSlotOption,
} from '../types/appointment-list.types';
import type { AvailableSlotsPayload } from '../types/slot-booking.types';

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

export async function fetchProposedRescheduleSlotsApi(
  appointment: AppointmentListItem,
): Promise<ProposedRescheduleSlotOption[]> {
  const { data } = await apiClient.get<AvailableSlotsPayload>('/appointments/available-slots', {
    params: {
      doctorId: appointment.doctorId || undefined,
      serviceId: appointment.serviceId || undefined,
    },
  });

  const activeDate =
    data.dates?.find((d) => d.status !== 'OFF' && d.status !== 'FULL') ?? data.dates?.[0];
  const allOpen = [...(data.morningSlots ?? []), ...(data.afternoonSlots ?? [])].filter(
    (s) =>
      s.status !== 'FULL' &&
      Boolean(s.id?.trim()) &&
      !(
        activeDate?.dateIso === appointment.scheduledDate &&
        s.timeRange === appointment.scheduledTimeRange
      ),
  );

  if (allOpen.length === 0) {
    return [];
  }

  return allOpen.slice(0, 3).map((slot, index) => {
    const startPart = slot.timeRange.split('-')[0]?.trim() ?? '08:00';
    return {
      slotId: slot.id,
      scheduledDate: activeDate?.dateIso ?? appointment.scheduledDate,
      scheduledDateBadge: activeDate
        ? `${activeDate.dayTopLabel}, ${activeDate.subDateLabel}`
        : appointment.scheduledDateBadge,
      scheduledDateFullLabel:
        activeDate?.fullDateDisplay ??
        `${appointment.scheduledDateBadge} (${slot.shift === 'AFTERNOON' ? 'Ca Chiều' : 'Ca Sáng'})`,
      scheduledTimeRange: slot.timeRange,
      checkInDeadlineTime: startPart,
      estimatedQueueNumber: String(index + 1).padStart(3, '0'),
      roomCode: appointment.roomCode,
      roomName: appointment.roomName,
      doctorName: `${appointment.doctorName} (${appointment.departmentName})`,
    };
  });
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
