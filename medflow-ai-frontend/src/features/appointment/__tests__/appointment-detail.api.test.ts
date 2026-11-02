import { AxiosError } from 'axios';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import {
  cancelAppointmentApi,
  fetchAppointmentDetailByIdApi,
  rescheduleAppointmentApi,
  updateAppointmentReasonApi,
} from '../api/appointment-detail.api';
import { MOCK_APPOINTMENTS } from '../data/appointment-list-mock';
import type { AppointmentListItem } from '../types/appointment-list.types';

describe('appointment-detail.api (HTTP + contract seams S2/S3)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('fetchAppointmentDetailByIdApi calls GET /appointments/:id with URL encoding and returns AppointmentDetailDto-shaped payload', async () => {
    localStorage.setItem('medflow.access_token', 'test-jwt-token');

    const detailDtoPayload = {
      ...MOCK_APPOINTMENTS[0],
      id: '15',
      appointmentCode: 'APT-202611-0015',
      status: 'BOOKED' as const,
      availableActions: ['CANCEL', 'RESCHEDULE', 'UPDATE_REASON', 'PRINT_SLIP', 'TRACK_QUEUE'],
    };

    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: detailDtoPayload });

    const result = await fetchAppointmentDetailByIdApi('APT-202611-0015');

    expect(getSpy).toHaveBeenCalledWith(`/appointments/${encodeURIComponent('APT-202611-0015')}`);
    expect(result).toEqual(detailDtoPayload);
  });

  it('cancelAppointmentApi calls POST /appointments/:id/cancel and returns backend AppointmentDetailDto without throwing (seam S2)', async () => {
    localStorage.setItem('medflow.access_token', 'test-jwt-token');

    // Seam S2: BE returns AppointmentDetailDto (which includes availableActions),
    // while FE types cancelAppointmentApi return as AppointmentListItem.
    const backendCancelDetailDto = {
      ...MOCK_APPOINTMENTS[0],
      id: '15',
      status: 'CANCELLED' as const,
      cancellationReason: 'Bận công tác đột xuất không thể đến',
      availableActions: ['REBOOK'],
    } as AppointmentListItem & { availableActions: string[] };

    const postSpy = vi
      .spyOn(apiClient, 'post')
      .mockResolvedValueOnce({ data: backendCancelDetailDto });

    const result = await cancelAppointmentApi({
      appointmentId: '15',
      cancellationReason: 'Bận công tác đột xuất không thể đến',
    });

    expect(postSpy).toHaveBeenCalledWith('/appointments/15/cancel', {
      cancellationReason: 'Bận công tác đột xuất không thể đến',
    });
    expect(result).toEqual(backendCancelDetailDto);
    expect(result.status).toBe('CANCELLED');
  });

  it('rescheduleAppointmentApi calls POST /appointments/:id/reschedule and returns backend AppointmentDetailDto without throwing (seam S2)', async () => {
    localStorage.setItem('medflow.access_token', 'test-jwt-token');

    // Seam S2: BE returns AppointmentDetailDto with the newly created replacement appointment id.
    const backendRescheduleDetailDto = {
      ...MOCK_APPOINTMENTS[0],
      id: '16',
      appointmentCode: 'APT-202611-0016',
      status: 'BOOKED' as const,
      availableActions: ['CANCEL', 'RESCHEDULE', 'UPDATE_REASON', 'PRINT_SLIP', 'TRACK_QUEUE'],
    } as AppointmentListItem & { availableActions: string[] };

    const postSpy = vi
      .spyOn(apiClient, 'post')
      .mockResolvedValueOnce({ data: backendRescheduleDetailDto });

    const result = await rescheduleAppointmentApi({
      appointmentId: '15',
      targetSlotId: '102',
      rescheduleNote: 'Đổi sang chiều',
    });

    expect(postSpy).toHaveBeenCalledWith('/appointments/15/reschedule', {
      targetSlotId: '102',
      rescheduleNote: 'Đổi sang chiều',
    });
    expect(result).toEqual(backendRescheduleDetailDto);
    expect(result.id).toBe('16');
  });

  it('updateAppointmentReasonApi falls back to mock on 404 because PATCH /appointments/:id/reason is absent in BE (seam S3)', async () => {
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

    const patchSpy = vi.spyOn(apiClient, 'patch').mockRejectedValueOnce(notFoundError);

    const result = await updateAppointmentReasonApi({
      appointmentId: '1',
      reasonForVisit: 'Triệu chứng cập nhật mới',
    });

    expect(patchSpy).toHaveBeenCalledWith('/appointments/1/reason', {
      reasonForVisit: 'Triệu chứng cập nhật mới',
    });
    expect(result.reasonForVisit).toBe('Triệu chứng cập nhật mới');
  });
});
