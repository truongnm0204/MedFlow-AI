import { apiClient } from '@/shared/lib/axios';

import type { JourneySlipDto, JourneySlipPrintResultDto } from '../types/journey-slip.types';

/**
 * GET /visits/{idOrCode}/journey-slip — minimal operational journey slip.
 * Auth: patient-owner OR same-hospital staff (401/403/404 handled by caller).
 */
export async function fetchJourneySlipApi(idOrCode: string): Promise<JourneySlipDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.get<JourneySlipDto>(`/visits/${encoded}/journey-slip`);
  return data;
}

/**
 * POST /visits/{idOrCode}/journey-slip/print — records print intent (audit JOURNEY_SLIP_PRINTED).
 */
export async function printJourneySlipApi(idOrCode: string): Promise<JourneySlipPrintResultDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<JourneySlipPrintResultDto>(
    `/visits/${encoded}/journey-slip/print`,
  );
  return data;
}
