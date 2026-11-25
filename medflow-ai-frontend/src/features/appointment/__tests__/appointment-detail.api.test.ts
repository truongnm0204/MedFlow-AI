import { AxiosError } from 'axios';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { ACCESS_TOKEN_KEY } from '@/shared/lib/auth-storage';
import { apiClient } from '@/shared/lib/axios';

import {
  cancelAppointmentApi,
  fetchAppointmentDetailByIdApi,
  fetchProposedRescheduleSlotsApi,
  fetchRescheduleScheduleApi,
  rescheduleAppointmentApi,
  updateAppointmentReasonApi,
} from '../api/appointment-detail.api';
import { MOCK_APPOINTMENTS } from '../data/appointment-list-mock';
import type { AppointmentListItem } from '../types/appointment-list.types';

describe('appointment-detail.api (pure HTTP + contract seams S2/S3)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('fetchAppointmentDetailByIdApi calls GET /appointments/:id with URL encoding and returns AppointmentDetailDto-shaped payload', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'test-jwt-token');

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

  it('fetchProposedRescheduleSlotsApi maps available slots and returns empty array when no open slot exists', async () => {
    const appointment = MOCK_APPOINTMENTS[0]!;
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        dates: [
          {
            dateIso: '2026-11-26',
            dayTopLabel: 'THỨ NĂM',
            subDateLabel: '26/11',
            fullDateDisplay: 'Thứ Năm, 26/11/2026',
            status: 'AVAILABLE',
          },
        ],
        morningSlots: [
          {
            id: 'slot-2026-1126-0830',
            timeRange: '08:30 - 09:00',
            shift: 'MORNING',
            status: 'AVAILABLE',
            remainingCount: 2,
          },
        ],
        afternoonSlots: [],
      },
    });

    const slots = await fetchProposedRescheduleSlotsApi(appointment);
    expect(getSpy).toHaveBeenCalledWith('/appointments/available-slots', {
      params: {
        doctorId: appointment.doctorId || undefined,
        serviceId: appointment.serviceId || undefined,
      },
    });
    expect(slots).toHaveLength(1);
    expect(slots[0]?.slotId).toBe('slot-2026-1126-0830');

    getSpy.mockResolvedValueOnce({
      data: { dates: [], morningSlots: [], afternoonSlots: [] },
    });
    await expect(fetchProposedRescheduleSlotsApi(appointment)).resolves.toEqual([]);
  });

  it('cancelAppointmentApi calls POST /appointments/:id/cancel and returns backend AppointmentDetailDto (seam S2)', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'test-jwt-token');

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

  it('rescheduleAppointmentApi calls POST /appointments/:id/reschedule and propagates 409 slot conflict', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'test-jwt-token');

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

    const conflictErr = new AxiosError(
      'Conflict',
      'ERR_BAD_REQUEST',
      undefined,
      {},
      {
        status: 409,
        statusText: 'Conflict',
        headers: {},
        config: {} as never,
        data: { status: 409, detail: 'Slot đã đầy' },
      },
    );
    postSpy.mockRejectedValueOnce(conflictErr);
    await expect(
      rescheduleAppointmentApi({ appointmentId: '15', targetSlotId: '102' }),
    ).rejects.toBe(conflictErr);
  });

  it('updateAppointmentReasonApi calls PATCH /appointments/:id/reason and rethrows HTTP 404/network errors without fake success', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'test-jwt-token');

    const updatedItem = {
      ...MOCK_APPOINTMENTS[0],
      reasonForVisit: 'Triệu chứng cập nhật mới',
    };
    const patchSpy = vi.spyOn(apiClient, 'patch').mockResolvedValueOnce({ data: updatedItem });

    const okResult = await updateAppointmentReasonApi({
      appointmentId: '1',
      reasonForVisit: 'Triệu chứng cập nhật mới',
    });
    expect(patchSpy).toHaveBeenCalledWith('/appointments/1/reason', {
      reasonForVisit: 'Triệu chứng cập nhật mới',
    });
    expect(okResult.reasonForVisit).toBe('Triệu chứng cập nhật mới');

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
    patchSpy.mockRejectedValueOnce(notFoundError);

    await expect(
      updateAppointmentReasonApi({
        appointmentId: '1',
        reasonForVisit: 'Triệu chứng cập nhật mới',
      }),
    ).rejects.toBe(notFoundError);
  });

  it('fetchRescheduleScheduleApi calls /appointments/service-shifts when doctorId is blank, preserves >3 slots, and sets estimatedQueueNumber to null', async () => {
    const serviceOnlyAppt: AppointmentListItem = {
      ...MOCK_APPOINTMENTS[0]!,
      doctorId: '',
      doctorName: '',
      serviceId: 'EX-GI-01',
      scheduledDate: '2026-11-26',
      scheduledTimeRange: '07:30 - 08:00',
    };

    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        monthLabel: 'Tháng 11, 2026',
        dates: [
          {
            dateIso: '2026-11-27',
            dayTopLabel: 'THỨ SÁU',
            subDateLabel: '27/11',
            fullDateDisplay: 'Thứ Sáu, 27/11/2026',
            status: 'AVAILABLE',
          },
        ],
        morningSlots: [
          {
            id: '101',
            timeRange: '08:00 - 08:30',
            shift: 'MORNING',
            status: 'AVAILABLE',
            remainingSlots: 3,
          },
          {
            id: '102',
            timeRange: '08:30 - 09:00',
            shift: 'MORNING',
            status: 'AVAILABLE',
            remainingSlots: 2,
          },
          {
            id: '103',
            timeRange: '09:00 - 09:30',
            shift: 'MORNING',
            status: 'AVAILABLE',
            remainingSlots: 4,
          },
          {
            id: '104',
            timeRange: '09:30 - 10:00',
            shift: 'MORNING',
            status: 'AVAILABLE',
            remainingSlots: 1,
          },
        ],
        afternoonSlots: [
          {
            id: '105',
            timeRange: '14:00 - 14:30',
            shift: 'AFTERNOON',
            status: 'AVAILABLE',
            remainingSlots: 2,
          },
        ],
      },
    });

    const schedule = await fetchRescheduleScheduleApi(serviceOnlyAppt, '2026-11-27');

    expect(getSpy).toHaveBeenCalledWith('/appointments/service-shifts', {
      params: {
        serviceId: 'EX-GI-01',
        dateIso: '2026-11-27',
      },
    });
    expect(schedule.slots).toHaveLength(5);
    expect(schedule.slots.every((s) => s.estimatedQueueNumber === null)).toBe(true);
    expect(schedule.slots[0]?.doctorName).toContain('Bác sĩ điều phối theo ca khám');
  });
});
