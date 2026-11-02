import { afterEach, describe, expect, it, vi } from 'vitest';

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

describe('review-booking.api (HTTP + contract seams S1/S4)', () => {
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
    dateIso: '2026-11-05',
    slotOrShiftId: '501',
    symptomNote: 'Đau thượng vị kéo dài',
  };

  it('confirmAppointmentBooking posts to /appointments and accepts BE status BOOKED when appointmentId is present (seam S1)', async () => {
    localStorage.setItem('medflow.access_token', 'test-jwt-token');

    // Seam S1: BE returns status: "BOOKED", whereas FE ConfirmBookingResponse declares status: 'CONFIRMED'.
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

  it('confirmAppointmentBooking rejects locally when slotOrShiftId is slot-conflict without calling HTTP (seam S4)', async () => {
    localStorage.setItem('medflow.access_token', 'test-jwt-token');
    const postSpy = vi.spyOn(apiClient, 'post');

    await expect(
      confirmAppointmentBooking({
        ...validRequest,
        slotOrShiftId: 'slot-conflict',
      }),
    ).rejects.toThrow(
      'Khung giờ khám vừa hết chỗ do bệnh nhân khác vừa hoàn tất đặt lịch. Vui lòng quay lại chọn khung giờ khác.',
    );

    expect(postSpy).not.toHaveBeenCalled();
  });

  it('confirmAppointmentBooking rejects locally when patientId is pat-unauthorized without calling HTTP', async () => {
    localStorage.setItem('medflow.access_token', 'test-jwt-token');
    const postSpy = vi.spyOn(apiClient, 'post');

    await expect(
      confirmAppointmentBooking({
        ...validRequest,
        patientId: 'pat-unauthorized',
      }),
    ).rejects.toThrow('Bạn không có quyền đặt lịch khám cho hồ sơ không thuộc quyền quản lý.');

    expect(postSpy).not.toHaveBeenCalled();
  });

  it('fetchAppointmentReviewPreview returns backend payload when patientInfo, scheduleLocation, and feeSummary are all present', async () => {
    localStorage.setItem('medflow.access_token', 'test-jwt-token');

    const params = {
      mode: 'service' as const,
      patientId: '1',
      serviceId: 'EX-GI-01',
      dateIso: '2026-11-05',
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

  it('fetchAppointmentReviewPreview falls back to mock when backend payload is missing feeSummary', async () => {
    localStorage.setItem('medflow.access_token', 'test-jwt-token');

    const params = {
      mode: 'doctor' as const,
      patientId: 'pat-self',
      doctorId: 'doc-01',
      serviceId: 'srv-01',
      dateIso: '2026-10-13',
      slotId: 'slot-0900',
    };

    const mockBase = getMockAppointmentReviewPayload(params);
    const incompletePayload = {
      patientInfo: mockBase.patientInfo,
      scheduleLocation: mockBase.scheduleLocation,
      feeSummary: undefined,
    } as unknown as AppointmentReviewPayload;

    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: incompletePayload });

    const result = await fetchAppointmentReviewPreview(params);

    expect(getSpy).toHaveBeenCalledWith('/appointments/review-preview', { params });
    expect(result).toEqual(mockBase);
  });
});
