import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import {
  openMockConsultationWorkspace,
  resolveMockConsultationWorkspace,
} from '../data/consultation-workspace-mock';
import type {
  ConsultationWorkspaceResponseDto,
  OpenConsultationWorkspaceRequest,
} from '../types/consultation-workspace.types';

/** Chỉ fallback về mock khi offline/network error (endpoint thực tế đã có và trả 4xx cho lỗi nghiệp vụ). */
function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  return !error.response;
}

function hasAccessToken(): boolean {
  return (
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'))
  );
}

/**
 * Xem không gian khám lâm sàng của lượt khám (`GET /doctor/consultation/{idOrCode}`).
 * Không thay đổi trạng thái DB (`actionOutcome = "VIEWED"`).
 */
export async function fetchConsultationWorkspace(
  idOrCode: string,
): Promise<ConsultationWorkspaceResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.get<ConsultationWorkspaceResponseDto>(
        `/doctor/consultation/${encoded}`,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return resolveMockConsultationWorkspace(idOrCode);
}

/**
 * Mở phiên khám lâm sàng (`POST /doctor/consultation/{idOrCode}/open`).
 * Chuyển lượt khám từ CALLED sang IN_PROGRESS, khởi tạo MedicalRecord DRAFT (revision 1).
 */
export async function openConsultationWorkspace(
  idOrCode: string,
  payload?: OpenConsultationWorkspaceRequest,
): Promise<ConsultationWorkspaceResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const body: Record<string, unknown> = {};

  if (payload?.expectedStateVersion !== undefined && payload.expectedStateVersion !== null) {
    body.expectedStateVersion = payload.expectedStateVersion;
  }
  if (payload?.notes !== undefined && payload.notes !== null && payload.notes.trim().length > 0) {
    body.notes = payload.notes.trim();
  }

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<ConsultationWorkspaceResponseDto>(
        `/doctor/consultation/${encoded}/open`,
        body,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return openMockConsultationWorkspace(idOrCode, payload);
}
