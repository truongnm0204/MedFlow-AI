import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import {
  createMockDiagnosticOrder,
  openMockConsultationWorkspace,
  recordMockClinicalExamination,
  resolveMockConsultationWorkspace,
  resolveMockDiagnosticOrderCatalog,
} from '../data/consultation-workspace-mock';
import type {
  ConsultationWorkspaceResponseDto,
  CreateDiagnosticOrderRequest,
  DiagnosticOrderCatalogResponseDto,
  OpenConsultationWorkspaceRequest,
  RecordClinicalExaminationRequest,
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

/**
 * Ghi nhận triệu chứng, sinh hiệu và khám lâm sàng (`POST /doctor/consultation/{idOrCode}/clinical-examination` — UC33 / #110).
 * Tạo phiên bản mới trong `medical_record_revisions` (append-only) và trả về workspace cập nhật.
 */
export async function recordClinicalExamination(
  idOrCode: string,
  payload: RecordClinicalExaminationRequest,
): Promise<ConsultationWorkspaceResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<ConsultationWorkspaceResponseDto>(
        `/doctor/consultation/${encoded}/clinical-examination`,
        payload,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return recordMockClinicalExamination(idOrCode, payload);
}

/**
 * Xem danh mục dịch vụ cận lâm sàng và các phiếu chỉ định hiện có của lượt khám
 * (`GET /doctor/consultation/{idOrCode}/diagnostic-orders` — UC34 / #120).
 */
export async function fetchDiagnosticOrderCatalog(
  idOrCode: string,
): Promise<DiagnosticOrderCatalogResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const response = await apiClient.get<DiagnosticOrderCatalogResponseDto>(
        `/doctor/consultation/${encoded}/diagnostic-orders`,
      );
      if (response?.data) {
        return response.data;
      }
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return resolveMockDiagnosticOrderCatalog(idOrCode);
}

/**
 * Tạo phiếu chỉ định cận lâm sàng và xếp hàng cận lâm sàng cho lượt khám
 * (`POST /doctor/consultation/{idOrCode}/diagnostic-orders` — UC34 / #120).
 */
export async function createDiagnosticOrder(
  idOrCode: string,
  payload: CreateDiagnosticOrderRequest,
): Promise<ConsultationWorkspaceResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<ConsultationWorkspaceResponseDto>(
        `/doctor/consultation/${encoded}/diagnostic-orders`,
        payload,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return createMockDiagnosticOrder(idOrCode, payload);
}
