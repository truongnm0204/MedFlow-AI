import { AxiosError } from 'axios';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { ACCESS_TOKEN_KEY } from '@/shared/lib/auth-storage';
import { apiClient } from '@/shared/lib/axios';

import {
  confirmAppointmentBooking,
  fetchAppointmentReviewPreview,
} from '../api/review-booking.api';
import { getMockAppointmentReviewPayload } from '../data/review-booking-mock';
import type {
  AppointmentReviewPayload,
  ConfirmBookingRequest,
  ConfirmBookingResponse,
} from '../types/review-booking.types';

describe('review-booking.api (pure HTTP + contract seams S1/S4)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  const validRequest: ConfirmBookingRequest = {
    mode: 'doctor',
    patientId: '1',
    doctorId: 'DOC-001',
    serviceId: 'EX-GI-01',
    serviceCode: 'EX-GI-01',
    dateIso: '2026-11-25',
    slotOrShiftId: '501',
    symptomNote: 'Đau thượng vị kéo dài',
  };

  it('confirmAppointmentBooking posts to /appointments and accepts BE status BOOKED when appointmentId is present (seam S1)', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'test-jwt-token');

    const backendResponse = {
      appointmentId: '42',
      appointmentCode: 'APT-202611-0042',
      queueNumber: '001',
      status: 'BOOKED',
    } as unknown as ConfirmBookingResponse;

    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: backendResponse });

    const result = await confirmAppointmentBooking(validRequest);

    expect(postSpy).toHaveBeenCalledWith('/appointments', validRequest);
    expect(result).toEqual(backendResponse);
    expect(result.appointmentId).toBe('42');
  });

  it('confirmAppointmentBooking rejects locally when required fields or symptomNote fail schema validation without calling HTTP', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'test-jwt-token');
    const postSpy = vi.spyOn(apiClient, 'post');

    await expect(
      confirmAppointmentBooking({
        ...validRequest,
        slotOrShiftId: '   ',
      }),
    ).rejects.toThrow('Vui lòng chọn khung giờ hoặc ca khám hợp lệ.');

    await expect(
      confirmAppointmentBooking({
        ...validRequest,
        symptomNote: 'ab',
      }),
    ).rejects.toThrow('Vui lòng mô tả lý do khám tối thiểu 3 ký tự.');

    expect(postSpy).not.toHaveBeenCalled();
  });

  it('confirmAppointmentBooking propagates HTTP 403 unauthorized-patient, HTTP 409 slot-conflict and network errors without fake confirmation', async () => {
    const forbiddenError = new AxiosError(
      'Forbidden',
      'ERR_BAD_REQUEST',
      undefined,
      {},
      {
        status: 403,
        statusText: 'Forbidden',
        headers: {},
        config: {} as never,
        data: {
          status: 403,
          detail: 'Bạn không có quyền đặt lịch khám cho hồ sơ không thuộc quyền quản lý.',
        },
      },
    );
    const postSpy = vi.spyOn(apiClient, 'post').mockRejectedValueOnce(forbiddenError);
    await expect(confirmAppointmentBooking(validRequest)).rejects.toBe(forbiddenError);

    const conflictError = new AxiosError(
      'Conflict',
      'ERR_BAD_REQUEST',
      undefined,
      {},
      {
        status: 409,
        statusText: 'Conflict',
        headers: {},
        config: {} as never,
        data: { status: 409, detail: 'Khung giờ khám vừa hết chỗ.' },
      },
    );
    postSpy.mockRejectedValueOnce(conflictError);
    await expect(confirmAppointmentBooking(validRequest)).rejects.toBe(conflictError);

    const networkError = new AxiosError('Network Error', 'ERR_NETWORK');
    postSpy.mockRejectedValueOnce(networkError);
    await expect(confirmAppointmentBooking(validRequest)).rejects.toBe(networkError);
  });

  it('fetchAppointmentReviewPreview returns backend payload when patientInfo, scheduleLocation, and feeSummary are all present', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'test-jwt-token');

    const params = {
      mode: 'service' as const,
      patientId: '1',
      serviceId: 'EX-GI-01',
      dateIso: '2026-11-25',
      shiftId: 'shift-morning',
    };

    const mockBase = getMockAppointmentReviewPayload(params);
    const backendPayload: AppointmentReviewPayload = {
      ...mockBase,
      patientInfo: {
        ...mockBase.patientInfo,
        patientId: '1',
        patientCode: 'PAT-0001',
      },
    };

    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: backendPayload });

    const result = await fetchAppointmentReviewPreview(params);

    expect(getSpy).toHaveBeenCalledWith('/appointments/review-preview', { params });
    expect(result).toEqual(backendPayload);
  });

  it('fetchAppointmentReviewPreview throws error when backend payload is incomplete or HTTP fails (no mock fallback)', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'test-jwt-token');

    const params = {
      mode: 'doctor' as const,
      patientId: 'pat-self',
      doctorId: 'doc-01',
      serviceId: 'srv-01',
      dateIso: '2026-11-25',
      slotId: 'slot-0900',
    };

    const mockBase = getMockAppointmentReviewPayload(params);
    const incompletePayload = {
      patientInfo: mockBase.patientInfo,
      scheduleLocation: mockBase.scheduleLocation,
      feeSummary: undefined,
    } as unknown as AppointmentReviewPayload;

    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: incompletePayload });

    await expect(fetchAppointmentReviewPreview(params)).rejects.toThrow(
      'Dữ liệu soát xét đặt lịch từ máy chủ chưa đầy đủ.',
    );

    const networkError = new AxiosError('Network Error', 'ERR_NETWORK');
    getSpy.mockRejectedValueOnce(networkError);
    await expect(fetchAppointmentReviewPreview(params)).rejects.toBe(networkError);
  });
});
