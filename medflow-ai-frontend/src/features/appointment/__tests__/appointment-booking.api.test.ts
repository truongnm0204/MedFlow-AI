import { AxiosError } from 'axios';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { ACCESS_TOKEN_KEY } from '@/shared/lib/auth-storage';
import { apiClient } from '@/shared/lib/axios';

import { createAppointmentApi } from '../api/appointment-booking.api';
import { MOCK_APPOINTMENTS } from '../data/appointment-list-mock';
import type { CreateAppointmentRequest } from '../types/booking-review.types';

describe('appointment-booking.api (pure HTTP)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  const validRequest: CreateAppointmentRequest = {
    mode: 'doctor',
    doctorId: 'doc-01',
    serviceId: 'srv-01',
    patientId: 'pat-self',
    dateIso: '2026-11-25',
    slotId: 'slot-0900',
    shiftId: null,
    reasonForVisit: 'Khám tiêu hóa định kỳ',
  };

  it('calls POST /appointments even when access token is absent in localStorage (no fake local booking)', async () => {
    const unauthorizedError = new AxiosError(
      'Unauthorized',
      'ERR_BAD_REQUEST',
      undefined,
      {},
      {
        status: 401,
        statusText: 'Unauthorized',
        headers: {},
        config: {} as never,
        data: { status: 401 },
      },
    );
    const postSpy = vi.spyOn(apiClient, 'post').mockRejectedValueOnce(unauthorizedError);

    await expect(createAppointmentApi(validRequest)).rejects.toBe(unauthorizedError);
    expect(postSpy).toHaveBeenCalledWith('/appointments', validRequest);
  });

  it('calls POST /appointments and returns backend payload on success', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'test-jwt-token');

    const backendPayload = {
      ...MOCK_APPOINTMENTS[0],
      id: '99',
      appointmentCode: 'APT-202611-0099',
    };

    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: backendPayload });

    const result = await createAppointmentApi(validRequest);

    expect(postSpy).toHaveBeenCalledWith('/appointments', validRequest);
    expect(result).toEqual(backendPayload);
  });

  it('propagates HTTP 409 Conflict (slot conflict), 404 and network errors without returning fake appointment', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'test-jwt-token');

    const conflictError = new AxiosError(
      'Slot conflict',
      'ERR_BAD_REQUEST',
      undefined,
      {},
      {
        status: 409,
        statusText: 'Conflict',
        headers: {},
        config: {} as never,
        data: { status: 409, detail: 'Khung giờ đã hết chỗ.' },
      },
    );
    vi.spyOn(apiClient, 'post').mockRejectedValueOnce(conflictError);
    await expect(createAppointmentApi(validRequest)).rejects.toBe(conflictError);

    const networkError = new AxiosError('Network Error', 'ERR_NETWORK');
    vi.spyOn(apiClient, 'post').mockRejectedValueOnce(networkError);
    await expect(createAppointmentApi(validRequest)).rejects.toBe(networkError);
  });
});
