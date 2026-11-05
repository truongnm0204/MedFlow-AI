import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { fetchExaminationTicket, printExaminationTicket } from '../api/visit-ticket.api';
import { buildFallbackExaminationTicket } from '../data/reception-mock-data';

describe('visit-ticket.api (UC29)', () => {
  beforeEach(() => {
    window.localStorage.setItem('medflow.access_token', 'test-reception-token');
  });

  afterEach(() => {
    window.localStorage.removeItem('medflow.access_token');
    vi.restoreAllMocks();
  });

  it('calls GET /reception/visits/{idOrCode}/ticket and returns backend DTO', async () => {
    const sampleTicket = buildFallbackExaminationTicket('VIS-202610-0001');
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: sampleTicket });

    const result = await fetchExaminationTicket('VIS-202610-0001');

    expect(getSpy).toHaveBeenCalledWith('/reception/visits/VIS-202610-0001/ticket');
    expect(result.visitCode).toBe('VIS-202610-0001');
    expect(result.ticketNo).toBe('A012');
  });

  it('calls POST /reception/visits/{idOrCode}/ticket/print with copies and reason', async () => {
    const printedTicket = {
      ...buildFallbackExaminationTicket('VIS-202610-0001'),
      copies: 2,
      printCount: 1,
      isReprint: false,
    };
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: printedTicket });

    const result = await printExaminationTicket('VIS-202610-0001', {
      copies: 2,
      reason: 'In phiếu tại quầy 1',
    });

    expect(postSpy).toHaveBeenCalledWith('/reception/visits/VIS-202610-0001/ticket/print', {
      copies: 2,
      reason: 'In phiếu tại quầy 1',
    });
    expect(result.copies).toBe(2);
    expect(result.printCount).toBe(1);
  });

  it('rethrows 409 Conflict when backend rejects unassigned doctor (UC26) or room (UC27)', async () => {
    const conflictError = new AxiosError(
      'Conflict',
      'ERR_BAD_REQUEST',
      {} as InternalAxiosRequestConfig,
      {},
      {
        status: 409,
        statusText: 'Conflict',
        headers: {},
        config: {} as InternalAxiosRequestConfig,
        data: {
          status: 409,
          detail:
            'Lượt khám chưa được phân công bác sĩ phụ trách (UC26). Vui lòng gán bác sĩ trước khi in phiếu khám.',
        },
      } as AxiosResponse,
    );

    vi.spyOn(apiClient, 'post').mockRejectedValueOnce(conflictError);

    await expect(printExaminationTicket('VIS-202610-0001')).rejects.toThrow();
  });
});
