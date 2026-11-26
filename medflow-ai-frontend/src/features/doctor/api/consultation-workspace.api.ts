import { apiClient } from '@/shared/lib/axios';

import type {
  ApproveOverdueReturnResultsRequest,
  CompleteVisitRequest,
  ConsultationWorkspaceResponseDto,
  CreateDiagnosticOrderRequest,
  CreatePrescriptionRequest,
  DiagnosticOrderCatalogResponseDto,
  Icd10CatalogItemDto,
  OpenConsultationWorkspaceRequest,
  RecordClinicalExaminationRequest,
  RecordDiagnosisRequest,
  RevalidateDiagnosticOrderRequest,
  SearchIcd10CodesParams,
} from '../types/consultation-workspace.types';
import type {
  DiagnosticResultsReviewResponseDto,
  ReviewDiagnosticResultsRequest,
} from '../types/review-diagnostic.types';

/**
 * Xem không gian khám lâm sàng của lượt khám (`GET /doctor/consultation/{idOrCode}`).
 * Không thay đổi trạng thái DB (`actionOutcome = "VIEWED"`).
 */
export async function fetchConsultationWorkspace(
  idOrCode: string,
): Promise<ConsultationWorkspaceResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.get<ConsultationWorkspaceResponseDto>(
    `/doctor/consultation/${encoded}`,
  );
  return data;
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

  const { data } = await apiClient.post<ConsultationWorkspaceResponseDto>(
    `/doctor/consultation/${encoded}/open`,
    body,
  );
  return data;
}

/**
 * Ghi nhận triệu chứng, sinh hiệu và khám lâm sàng (`POST /doctor/consultation/{idOrCode}/clinical-examination` — UC33 / #110).
 */
export async function recordClinicalExamination(
  idOrCode: string,
  payload: RecordClinicalExaminationRequest,
): Promise<ConsultationWorkspaceResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<ConsultationWorkspaceResponseDto>(
    `/doctor/consultation/${encoded}/clinical-examination`,
    payload,
  );
  return data;
}

/**
 * Ghi nhận chẩn đoán ICD-10 và kết luận lâm sàng
 * (`POST /doctor/consultation/{idOrCode}/diagnosis` — UC36 / #112).
 */
export async function recordDiagnosis(
  idOrCode: string,
  payload: RecordDiagnosisRequest,
): Promise<ConsultationWorkspaceResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<ConsultationWorkspaceResponseDto>(
    `/doctor/consultation/${encoded}/diagnosis`,
    payload,
  );
  return data;
}

/**
 * Tìm mã ICD-10 (`GET /doctor/icd10-codes` — UC36 / #112).
 */
export async function searchIcd10Codes(
  params: SearchIcd10CodesParams,
): Promise<Icd10CatalogItemDto[]> {
  const { data } = await apiClient.get<Icd10CatalogItemDto[]>('/doctor/icd10-codes', {
    params: {
      q: params.q?.trim() || undefined,
      primaryOnly: params.primaryOnly || undefined,
      limit: params.limit ?? 20,
    },
  });
  return data;
}

/**
 * Lưu đơn thuốc nháp (`PUT /doctor/consultation/{idOrCode}/prescription` — UC37 / #140).
 */
export async function savePrescription(
  idOrCode: string,
  payload: CreatePrescriptionRequest,
): Promise<ConsultationWorkspaceResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.put<ConsultationWorkspaceResponseDto>(
    `/doctor/consultation/${encoded}/prescription`,
    payload,
  );
  return data;
}

/**
 * Xem danh mục dịch vụ cận lâm sàng và các phiếu chỉ định hiện có của lượt khám
 * (`GET /doctor/consultation/{idOrCode}/diagnostic-orders` — UC34 / #120).
 */
export async function fetchDiagnosticOrderCatalog(
  idOrCode: string,
): Promise<DiagnosticOrderCatalogResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.get<DiagnosticOrderCatalogResponseDto>(
    `/doctor/consultation/${encoded}/diagnostic-orders`,
  );
  return data;
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
  const { data } = await apiClient.post<ConsultationWorkspaceResponseDto>(
    `/doctor/consultation/${encoded}/diagnostic-orders`,
    payload,
  );
  return data;
}

/**
 * Hoàn tất lượt khám lâm sàng và khóa hồ sơ bệnh án (`POST /doctor/consultation/{idOrCode}/complete` — UC38 / #117).
 */
export async function completeVisit(
  idOrCode: string,
  payload?: CompleteVisitRequest,
): Promise<ConsultationWorkspaceResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<ConsultationWorkspaceResponseDto>(
    `/doctor/consultation/${encoded}/complete`,
    payload ?? {},
  );
  return data;
}

/**
 * Xem kết quả cận lâm sàng của lượt khám để bác sĩ xác nhận đã xem xét
 * (`GET /doctor/consultation/{idOrCode}/diagnostic-results` — UC35 / #113).
 */
export async function fetchDiagnosticResultsReview(
  idOrCode: string,
): Promise<DiagnosticResultsReviewResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.get<DiagnosticResultsReviewResponseDto>(
    `/doctor/consultation/${encoded}/diagnostic-results`,
  );
  return data;
}

/**
 * Ghi nhận bác sĩ đã xem xét kết quả cận lâm sàng
 * (`POST /doctor/consultation/{idOrCode}/diagnostic-results/review` — UC35 / #113).
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

  const { data } = await apiClient.post<ConsultationWorkspaceResponseDto>(
    `/doctor/consultation/${encoded}/diagnostic-results/review`,
    body,
  );
  return data;
}

/**
 * Bác sĩ phụ trách gia hạn hiệu lực chuyên môn cho phiếu chỉ định cận lâm sàng quá hạn
 * (`POST /doctor/consultation/{idOrCode}/diagnostic-orders/{orderIdOrCode}/revalidate` — ADR-0061 / BUG-04).
 */
export async function revalidateDiagnosticOrder(
  idOrCode: string,
  orderIdOrCode: number | string,
  payload: RevalidateDiagnosticOrderRequest,
): Promise<ConsultationWorkspaceResponseDto> {
  const encodedVisit = encodeURIComponent(idOrCode.trim());
  const encodedOrder = encodeURIComponent(String(orderIdOrCode).trim());
  const { data } = await apiClient.post<ConsultationWorkspaceResponseDto>(
    `/doctor/consultation/${encodedVisit}/diagnostic-orders/${encodedOrder}/revalidate`,
    payload,
  );
  return data;
}

/**
 * Bác sĩ phụ trách xác nhận giá trị chuyên môn của tập kết quả cận lâm sàng hiện hành khi khám kết luận quá hạn
 * (`POST /doctor/consultation/{idOrCode}/approve-overdue-results` — ADR-0061 / BUG-04).
 */
export async function approveOverdueReturnResults(
  idOrCode: string,
  payload: ApproveOverdueReturnResultsRequest,
): Promise<ConsultationWorkspaceResponseDto> {
  const encodedVisit = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<ConsultationWorkspaceResponseDto>(
    `/doctor/consultation/${encodedVisit}/approve-overdue-results`,
    payload,
  );
  return data;
}
