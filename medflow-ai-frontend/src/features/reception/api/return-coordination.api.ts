import { apiClient } from '@/shared/lib/axios';

import type {
  CreateReturnEnrollmentRequestDto,
  ReturnCoordinationContextDto,
  ReturnEnrollmentResponseDto,
} from '../types/return-coordination.types';

/**
 * GET /reception/visits/{idOrCode}/return-coordination-context
 * Loads the centralized return-coordination context for a visit (Stage E2 / RC-04..09).
 */
export async function fetchReturnCoordinationContextApi(
  visitIdOrCode: string,
): Promise<ReturnCoordinationContextDto> {
  const encoded = encodeURIComponent(visitIdOrCode.trim());
  const { data } = await apiClient.get<ReturnCoordinationContextDto>(
    `/reception/visits/${encoded}/return-coordination-context`,
  );
  return data;
}

/**
 * POST /reception/visits/{idOrCode}/return-enrollment
 * Coordinates a doctor and issues the RETURN_CONSULTATION ticket (Stage E2 / RC-01..22).
 * Reception / Hospital Admin only — all other roles get 403 [RETURN_ENROLLMENT_RECEPTION_REQUIRED].
 */
export async function enrollReturnConsultationApi(
  visitIdOrCode: string,
  payload: CreateReturnEnrollmentRequestDto,
): Promise<ReturnEnrollmentResponseDto> {
  const encoded = encodeURIComponent(visitIdOrCode.trim());
  const { data } = await apiClient.post<ReturnEnrollmentResponseDto>(
    `/reception/visits/${encoded}/return-enrollment`,
    payload,
  );
  return data;
}
