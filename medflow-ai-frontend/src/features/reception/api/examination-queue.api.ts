import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { buildMockExaminationQueueResponse } from '../data/examination-queue-mock';
import type {
  ExaminationQueueQueryParams,
  ExaminationQueueResponseDto,
} from '../types/examination-queue.types';

function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

export async function fetchExaminationQueue(
  params?: ExaminationQueueQueryParams,
): Promise<ExaminationQueueResponseDto> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.get<ExaminationQueueResponseDto>(
        '/reception/examination-queue',
        params ? { params } : undefined,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return buildMockExaminationQueueResponse(params);
}
