import { apiClient } from '@/shared/lib/axios';

import { confirmBookingSchema } from '../schemas/review-booking.schema';
import type {
  AppointmentReviewPayload,
  ConfirmBookingRequest,
  ConfirmBookingResponse,
  ReviewBookingQueryParams,
} from '../types/review-booking.types';

export async function fetchAppointmentReviewPreview(
  params: ReviewBookingQueryParams,
): Promise<AppointmentReviewPayload> {
  const { data } = await apiClient.get<AppointmentReviewPayload>('/appointments/review-preview', {
    params,
  });

  if (!data || !data.patientInfo || !data.scheduleLocation || !data.feeSummary) {
    throw new Error('Dữ liệu soát xét đặt lịch từ máy chủ chưa đầy đủ.');
  }

  return data;
}

export async function confirmAppointmentBooking(
  payload: ConfirmBookingRequest,
): Promise<ConfirmBookingResponse> {
  const parsed = confirmBookingSchema.safeParse(payload);
  if (!parsed.success) {
    throw new Error(parsed.error.issues[0]?.message ?? 'Thông tin xác nhận đặt lịch không hợp lệ.');
  }

  const { data } = await apiClient.post<ConfirmBookingResponse>('/appointments', parsed.data);
  if (!data || !data.appointmentId) {
    throw new Error('Máy chủ không trả về mã lịch hẹn hợp lệ sau khi xác nhận.');
  }

  return data;
}
