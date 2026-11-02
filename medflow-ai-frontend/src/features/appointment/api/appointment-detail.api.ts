import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { MOCK_APPOINTMENTS, MOCK_PROPOSED_RESCHEDULE_SLOTS } from '../data/appointment-list-mock';
import type {
  AppointmentListItem,
  ProposedRescheduleSlotOption,
} from '../types/appointment-list.types';
import type { AvailableSlotsPayload } from '../types/slot-booking.types';

function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

function findMockAppointment(idOrCode: string): AppointmentListItem | undefined {
  const normalized = idOrCode.trim().replace(/^#/, '').toLowerCase();
  if (!normalized) return undefined;

  const direct = MOCK_APPOINTMENTS.find(
    (item) =>
      item.id.toLowerCase() === normalized || item.appointmentCode.toLowerCase() === normalized,
  );
  if (direct) return direct;
  if (normalized === '1') return MOCK_APPOINTMENTS[0];
  if (normalized === '2') return MOCK_APPOINTMENTS[1];
  if (normalized === '3') return MOCK_APPOINTMENTS[2];

  // Hỗ trợ liên kết từ thẻ Lịch hẹn sắp tới trên Trang chủ (HomePage mock)
  if (normalized === 'apt-2026-0108' || normalized === 'mf-261028-08') {
    return MOCK_APPOINTMENTS[0];
  }

  return undefined;
}

export async function fetchAppointmentDetailByIdApi(
  appointmentId: string,
): Promise<AppointmentListItem> {
  const cleanId = appointmentId.trim();
  if (!cleanId) {
    throw new Error('Mã lịch hẹn không hợp lệ.');
  }

  if (cleanId.toLowerCase() === 'apt-unauthorized' || cleanId.toLowerCase() === 'forbidden') {
    throw new Error('Bạn không có quyền xem chi tiết lịch hẹn của tài khoản bệnh nhân khác.');
  }

  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const response = await apiClient.get<AppointmentListItem>(
        `/appointments/${encodeURIComponent(cleanId)}`,
      );
      return response.data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  const found = findMockAppointment(cleanId);
  if (!found) {
    throw new Error(`Không tìm thấy phiếu hẹn khám ngoại trú với mã "${cleanId}".`);
  }

  return { ...found };
}

export function getProposedRescheduleSlots(
  appointment: AppointmentListItem,
): ProposedRescheduleSlotOption[] {
  return MOCK_PROPOSED_RESCHEDULE_SLOTS.map((slot) => ({
    ...slot,
    roomCode: appointment.roomCode || slot.roomCode,
    roomName: appointment.roomName || slot.roomName,
    doctorName: `${appointment.doctorName} (${appointment.departmentName})`,
  }));
}

export async function fetchProposedRescheduleSlotsApi(
  appointment: AppointmentListItem,
): Promise<ProposedRescheduleSlotOption[]> {
  const fallback = getProposedRescheduleSlots(appointment);
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (!hasToken) return fallback;

  try {
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
        /^\d+$/.test(s.id) &&
        !(
          activeDate?.dateIso === appointment.scheduledDate &&
          s.timeRange === appointment.scheduledTimeRange
        ),
    );

    if (allOpen.length === 0) return fallback;

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
  } catch {
    return fallback;
  }
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
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const response = await apiClient.post<AppointmentListItem>(
        `/appointments/${encodeURIComponent(appointmentId)}/reschedule`,
        { targetSlotId, rescheduleNote },
      );
      return response.data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  const target = findMockAppointment(appointmentId);
  if (!target) {
    throw new Error(`Không tìm thấy lịch hẹn "${appointmentId}" để đổi lịch.`);
  }

  const slot = MOCK_PROPOSED_RESCHEDULE_SLOTS.find((s) => s.slotId === targetSlotId);
  if (!slot) {
    throw new Error('Khung giờ đề xuất không hợp lệ hoặc đã hết chỗ.');
  }

  target.scheduledDate = slot.scheduledDate;
  target.scheduledDateBadge = slot.scheduledDateBadge;
  target.scheduledDateFullLabel = slot.scheduledDateFullLabel;
  target.scheduledTimeRange = slot.scheduledTimeRange;
  target.checkInDeadlineTime = slot.checkInDeadlineTime;
  target.queueNumber = slot.estimatedQueueNumber;
  target.status = 'CONFIRMED';
  target.statusBannerLabel = 'ĐÃ XÁC NHẬN - ĐÃ ĐỔI LỊCH KHÁM';
  if (rescheduleNote?.trim()) {
    target.reasonForVisit = `${target.reasonForVisit ? `${target.reasonForVisit} • ` : ''}Ghi chú đổi lịch: ${rescheduleNote.trim()}`;
  }

  return { ...target };
}

export interface CancelAppointmentPayload {
  appointmentId: string;
  cancellationReason: string;
}

export async function cancelAppointmentApi({
  appointmentId,
  cancellationReason,
}: CancelAppointmentPayload): Promise<AppointmentListItem> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const response = await apiClient.post<AppointmentListItem>(
        `/appointments/${encodeURIComponent(appointmentId)}/cancel`,
        { cancellationReason },
      );
      return response.data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  const target = findMockAppointment(appointmentId);
  if (!target) {
    throw new Error(`Không tìm thấy lịch hẹn "${appointmentId}" để hủy.`);
  }

  target.status = 'CANCELLED';
  target.statusBannerLabel = 'ĐÃ HỦY BỞI NGƯỜI BỆNH';
  target.queueNumber = null;
  target.paymentStatus = 'REFUNDED_OR_FREE';
  target.paymentStatusLabel = 'Giải phóng slot: Đã hoàn tất, không phát sinh chi phí';
  target.slotReleaseNote = 'Giải phóng slot: Đã hoàn tất, không phát sinh chi phí';
  target.cancellationReason = cancellationReason.trim();
  target.rebookSpecialtyLabel = `Đặt lại ${target.specialtyName}`;

  return { ...target };
}

export interface UpdateAppointmentReasonPayload {
  appointmentId: string;
  reasonForVisit: string;
}

export async function updateAppointmentReasonApi({
  appointmentId,
  reasonForVisit,
}: UpdateAppointmentReasonPayload): Promise<AppointmentListItem> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const response = await apiClient.patch<AppointmentListItem>(
        `/appointments/${encodeURIComponent(appointmentId)}/reason`,
        { reasonForVisit },
      );
      return response.data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  const target = findMockAppointment(appointmentId);
  if (!target) {
    throw new Error(`Không tìm thấy lịch hẹn "${appointmentId}" để cập nhật triệu chứng.`);
  }

  target.reasonForVisit = reasonForVisit.trim();
  return { ...target };
}
