import { AxiosError } from 'axios';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { createAppointmentApi } from '../api/appointment-booking.api';
import { MOCK_APPOINTMENTS } from '../data/appointment-list-mock';
import type { CreateAppointmentRequest } from '../types/booking-review.types';

describe('appointment-booking.api (HTTP + mock fallback)', () => {
  const initialMockLength = MOCK_APPOINTMENTS.length;

  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    while (MOCK_APPOINTMENTS.length > initialMockLength) {
      MOCK_APPOINTMENTS.shift();
    }
  });

  const validRequest: CreateAppointmentRequest = {
    mode: 'doctor',
    doctorId: 'doc-01',
    serviceId: 'srv-01',
    patientId: 'pat-self',
    dateIso: '2026-10-13',
    slotId: 'slot-0900',
    shiftId: null,
    reasonForVisit: 'Khám tiêu hóa định kỳ',
  };

  it('creates mock appointment without calling apiClient.post when access token is absent', async () => {
    const postSpy = vi.spyOn(apiClient, 'post');

    const result = await createAppointmentApi(validRequest);

    expect(postSpy).not.toHaveBeenCalled();
    expect(result.scheduledDate).toBe('2026-10-13');
    expect(result.status).toBe('CONFIRMED');
  });

  it('calls POST /appointments and returns backend payload when access token is present', async () => {
    localStorage.setItem('medflow.access_token', 'test-jwt-token');

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

  it('falls back to createMockAppointment when POST /appointments rejects with 404 AxiosError', async () => {
    localStorage.setItem('medflow.access_token', 'test-jwt-token');

    const notFoundError = new AxiosError(
      'Request failed with status code 404',
      'ERR_BAD_REQUEST',
      undefined,
      undefined,
      {
        status: 404,
        statusText: 'Not Found',
        headers: {},
        config: {} as never,
        data: { status: 404 },
      },
    );

    const postSpy = vi.spyOn(apiClient, 'post').mockRejectedValueOnce(notFoundError);

    const result = await createAppointmentApi(validRequest);

    expect(postSpy).toHaveBeenCalledWith('/appointments', validRequest);
    expect(result.appointmentCode).toMatch(/^APT-2026-1013-/);
  });
});
