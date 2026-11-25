import { apiClient } from '@/shared/lib/axios';

import type {
  CallNextPatientRequestPayload,
  CallNextPatientResponseDto,
  CreateReturnToDoctorQueueEntryRequestPayload,
  CreateReturnToDoctorQueueEntryResponseDto,
  EarlyArrivalBackfillRequestPayload,
  EarlyArrivalBackfillResponseDto,
  ExaminationQueueQueryParams,
  ExaminationQueueResponseDto,
  MarkMissedTurnRequestPayload,
  QueueTurnActionResponseDto,
  RestoreQueueTurnRequestPayload,
  UpdateExaminationQueueStatusPayload,
  UpdateExaminationQueueStatusResponseDto,
} from '../types/examination-queue.types';

export async function fetchExaminationQueue(
  params?: ExaminationQueueQueryParams,
): Promise<ExaminationQueueResponseDto> {
  const { data } = await apiClient.get<ExaminationQueueResponseDto>(
    '/reception/examination-queue',
    params ? { params } : undefined,
  );
  return data;
}

export async function applyEarlyArrivalBackfill(
  queueEntryId: string,
  payload: EarlyArrivalBackfillRequestPayload,
): Promise<EarlyArrivalBackfillResponseDto> {
  const normalizedId = queueEntryId.trim();
  const { data } = await apiClient.post<EarlyArrivalBackfillResponseDto>(
    `/reception/examination-queue/entries/${encodeURIComponent(normalizedId)}/early-arrival-backfill`,
    payload,
  );
  return data;
}

export async function updateExaminationQueueStatus(
  queueEntryId: string,
  payload: UpdateExaminationQueueStatusPayload,
  _currentQueueData?: ExaminationQueueResponseDto,
): Promise<UpdateExaminationQueueStatusResponseDto> {
  const encodedId = encodeURIComponent(queueEntryId.trim());
  const { data } = await apiClient.patch<UpdateExaminationQueueStatusResponseDto>(
    `/reception/examination-queue/entries/${encodedId}/status`,
    payload,
  );
  return data;
}

export async function markQueueEntryMissedTurn(
  idOrCode: string,
  payload: MarkMissedTurnRequestPayload,
): Promise<QueueTurnActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<QueueTurnActionResponseDto>(
    `/reception/examination-queue/entries/${encoded}/missed-turn`,
    payload,
  );
  return data;
}

export async function restoreSkippedQueueTurn(
  idOrCode: string,
  payload: RestoreQueueTurnRequestPayload,
): Promise<QueueTurnActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<QueueTurnActionResponseDto>(
    `/reception/examination-queue/entries/${encoded}/restore-turn`,
    payload,
  );
  return data;
}

export async function callNextEligiblePatient(
  servicePointId: string,
  payload?: CallNextPatientRequestPayload,
): Promise<CallNextPatientResponseDto> {
  const encoded = encodeURIComponent(servicePointId.trim());
  const body = payload?.notes !== undefined ? { notes: payload.notes } : {};
  const { data } = await apiClient.post<CallNextPatientResponseDto>(
    `/queue/service-points/${encoded}/call-next`,
    body,
  );
  return data;
}

export async function createReturnToDoctorQueueEntry(
  idOrCode: string,
  payload: CreateReturnToDoctorQueueEntryRequestPayload,
): Promise<CreateReturnToDoctorQueueEntryResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<CreateReturnToDoctorQueueEntryResponseDto>(
    `/reception/examination-queue/entries/${encoded}/return-to-doctor`,
    payload,
  );
  return data;
}
