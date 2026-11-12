import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { searchMockIcd10Codes } from '../data/diagnosis-conclusion-mock';
import {
  completeMockVisit,
  createMockDiagnosticOrder,
  openMockConsultationWorkspace,
  recordMockClinicalExamination,
  recordMockDiagnosis,
  resolveMockConsultationWorkspace,
  resolveMockDiagnosticOrderCatalog,
} from '../data/consultation-workspace-mock';
import {
  resolveMockDiagnosticResultsReview,
  reviewMockDiagnosticResults,
} from '../data/review-diagnostic-mock';
import type {
  CompleteVisitRequest,
  ConsultationWorkspaceResponseDto,
  CreateDiagnosticOrderRequest,
  DiagnosticOrderCatalogResponseDto,
  Icd10CatalogItemDto,
  OpenConsultationWorkspaceRequest,
  RecordClinicalExaminationRequest,
  RecordDiagnosisRequest,
  SearchIcd10CodesParams,
} from '../types/consultation-workspace.types';
import type {
  DiagnosticResultsReviewResponseDto,
  ReviewDiagnosticResultsRequest,
} from '../types/review-diagnostic.types';
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
 * Ghi nhận chẩn đoán ICD-10 và kết luận lâm sàng
 * (`POST /doctor/consultation/{idOrCode}/diagnosis` — UC36 / #112).
 * Có `medflow.access_token` thì mọi lỗi HTTP và lỗi mạng đều throw. Không token mới dùng mock.
 */
export async function recordDiagnosis(
  idOrCode: string,
  payload: RecordDiagnosisRequest,
): Promise<ConsultationWorkspaceResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    const { data } = await apiClient.post<ConsultationWorkspaceResponseDto>(
      `/doctor/consultation/${encoded}/diagnosis`,
      payload,
    );
    return data;
  }

  return recordMockDiagnosis(idOrCode, payload);
}

/**
 * Tìm mã ICD-10 (`GET /doctor/icd10-codes` — UC36 / #112).
 * `q` tối đa 100 ký tự, `limit` 1–200. Danh mục không có `nameEn`.
 * Có `medflow.access_token` thì mọi lỗi HTTP và lỗi mạng đều throw. Không token mới dùng mock.
 */
export async function searchIcd10Codes(
  params: SearchIcd10CodesParams,
): Promise<Icd10CatalogItemDto[]> {
  if (hasAccessToken()) {
    const { data } = await apiClient.get<Icd10CatalogItemDto[]>('/doctor/icd10-codes', {
      params: {
        q: params.q?.trim() || undefined,
        primaryOnly: params.primaryOnly || undefined,
        limit: params.limit ?? 20,
      },
    });
    return data;
  }

  return searchMockIcd10Codes(params);
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

/**
 * Hoàn tất lượt khám lâm sàng và khóa hồ sơ bệnh án (`POST /doctor/consultation/{idOrCode}/complete` — UC38 / #117).
 */
export async function completeVisit(
  idOrCode: string,
  payload?: CompleteVisitRequest,
): Promise<ConsultationWorkspaceResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<ConsultationWorkspaceResponseDto>(
        `/doctor/consultation/${encoded}/complete`,
        payload ?? {},
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return completeMockVisit(idOrCode, payload);
}

/**
 * Xem kết quả cận lâm sàng của lượt khám để bác sĩ xác nhận đã xem xét
 * (`GET /doctor/consultation/{idOrCode}/diagnostic-results` — UC35 / #113).
 * Có `medflow.access_token` thì mọi lỗi HTTP và lỗi mạng đều throw. Không token mới dùng mock.
 */
export async function fetchDiagnosticResultsReview(
  idOrCode: string,
): Promise<DiagnosticResultsReviewResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    const { data } = await apiClient.get<DiagnosticResultsReviewResponseDto>(
      `/doctor/consultation/${encoded}/diagnostic-results`,
    );
    return data;
  }

  return resolveMockDiagnosticResultsReview(idOrCode);
}

/**
 * Ghi nhận bác sĩ đã xem xét kết quả cận lâm sàng
 * (`POST /doctor/consultation/{idOrCode}/diagnostic-results/review` — UC35 / #113).
 * Có `medflow.access_token` thì mọi lỗi HTTP và lỗi mạng đều throw. Không token mới dùng mock.
 */
export async function reviewDiagnosticResults(
  idOrCode: string,
  payload: ReviewDiagnosticResultsRequest,
): Promise<ConsultationWorkspaceResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const body: Record<string, unknown> = {};

  if (payload.orderIds && payload.orderIds.length > 0) {
    body.orderIds = payload.orderIds;
  }
  if (payload.orderCodes && payload.orderCodes.length > 0) {
    body.orderCodes = payload.orderCodes;
  }

  const clinicalInterpretation = payload.clinicalInterpretation?.trim();
  if (clinicalInterpretation) {
    body.clinicalInterpretation = clinicalInterpretation;
  }
  const reviewNote = payload.reviewNote?.trim();
  if (reviewNote) {
    body.reviewNote = reviewNote;
  }
  const clinicalConclusion = payload.clinicalConclusion?.trim();
  if (clinicalConclusion) {
    body.clinicalConclusion = clinicalConclusion;
  }
  const amendmentReason = payload.amendmentReason?.trim();
  if (amendmentReason) {
    body.amendmentReason = amendmentReason;
  }
  if (payload.expectedStateVersion !== undefined && payload.expectedStateVersion !== null) {
    body.expectedStateVersion = payload.expectedStateVersion;
  }
  if (payload.expectedRevisionNo !== undefined && payload.expectedRevisionNo !== null) {
    body.expectedRevisionNo = payload.expectedRevisionNo;
  }

  if (hasAccessToken()) {
    const { data } = await apiClient.post<ConsultationWorkspaceResponseDto>(
      `/doctor/consultation/${encoded}/diagnostic-results/review`,
      body,
    );
    return data;
  }

  return reviewMockDiagnosticResults(idOrCode, payload);
}
