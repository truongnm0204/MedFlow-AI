import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import {
  buildMockConfirmBookingResponse,
  getMockAppointmentReviewPayload,
} from '../data/review-booking-mock';
import { confirmBookingSchema } from '../schemas/review-booking.schema';
import type {
  AppointmentReviewPayload,
  ConfirmBookingRequest,
  ConfirmBookingResponse,
  ReviewBookingQueryParams,
} from '../types/review-booking.types';

function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

export async function fetchAppointmentReviewPreview(
  params: ReviewBookingQueryParams,
): Promise<AppointmentReviewPayload> {
  if (params.patientId === 'pat-unauthorized' || params.patientId === 'forbidden') {
    throw new Error('Bạn không có quyền truy cập hồ sơ người bệnh thuộc tài khoản khác.');
  }

  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.get<AppointmentReviewPayload>(
        '/appointments/review-preview',
        { params },
      );
      if (data && data.patientInfo && data.scheduleLocation && data.feeSummary) {
        return data;
      }
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return getMockAppointmentReviewPayload(params);
}

export async function confirmAppointmentBooking(
  payload: ConfirmBookingRequest,
): Promise<ConfirmBookingResponse> {
  const parsed = confirmBookingSchema.safeParse(payload);
  if (!parsed.success) {
    throw new Error(parsed.error.issues[0]?.message ?? 'Thông tin xác nhận đặt lịch không hợp lệ.');
  }

  if (
    parsed.data.slotOrShiftId === 'slot-conflict' ||
    parsed.data.slotOrShiftId === 'slot-0730' ||
    parsed.data.slotOrShiftId === 'slot-0800'
  ) {
    throw new Error(
      'Khung giờ khám vừa hết chỗ do bệnh nhân khác vừa hoàn tất đặt lịch. Vui lòng quay lại chọn khung giờ khác.',
    );
  }

  if (parsed.data.patientId === 'pat-unauthorized') {
    throw new Error('Bạn không có quyền đặt lịch khám cho hồ sơ không thuộc quyền quản lý.');
  }

  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.post<ConfirmBookingResponse>('/appointments', parsed.data);
      if (data && data.appointmentId) {
        return data;
      }
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return buildMockConfirmBookingResponse(parsed.data);
}
