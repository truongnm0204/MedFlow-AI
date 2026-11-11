import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { DIAGNOSTIC_DASHBOARD_MOCK } from '../data/diagnostic-mock-data';
import {
  confirmMockDiagnosticOrder,
  confirmMockDiagnosticOrdersBatch,
  resolveMockDiagnosticOrderReviewDetail,
  resolveMockDiagnosticOrdersForReview,
} from '../data/diagnostic-order-review-mock';
import {
  completeMockDiagnosticServiceExecution,
  resolveMockDiagnosticExecutionDetail,
  resolveMockDiagnosticExecutions,
  startMockDiagnosticServiceExecution,
  updateMockDiagnosticServiceStatus,
  updateMockDiagnosticServiceStatusBatch,
} from '../data/diagnostic-execution-mock';
import {
  enterMockDiagnosticResult,
  resolveMockDiagnosticOrdersForResultEntry,
  resolveMockDiagnosticResultEntryDetail,
} from '../data/diagnostic-result-entry-mock';
import {
  receiveMockDiagnosticPatient,
  receiveMockDiagnosticPatientsBatch,
  resolveMockDiagnosticOrdersForReceive,
  resolveMockDiagnosticPatientReceiveDetail,
} from '../data/diagnostic-patient-receive-mock';
import {
  callNextMockDiagnosticPatient,
  cancelMockDiagnosticQueueEntry,
  markMockDiagnosticMissedTurn,
  resolveMockDiagnosticQueueBoard,
  resolveMockDiagnosticQueueEntryDetail,
  restoreMockDiagnosticSkippedTurn,
  updateMockDiagnosticQueueStatus,
} from '../data/diagnostic-queue-mock';
import {
  resolveMockDiagnosticWorklist,
  resolveMockDiagnosticWorklistOrderDetail,
} from '../data/diagnostic-worklist-mock';
import { diagnosticOrderIdentifierSchema } from '../schemas/diagnostic-worklist-filter.schema';
import type {
  CallNextDiagnosticPatientRequest,
  CallNextDiagnosticPatientResponseDto,
  CancelDiagnosticQueueEntryRequest,
  ConfirmDiagnosticOrderRequest,
  ConfirmDiagnosticOrderResponseDto,
  ConfirmDiagnosticOrdersBatchRequest,
  DiagnosticDashboardQueryParams,
  DiagnosticDashboardSummary,
  DiagnosticOrderReviewDetailResponseDto,
  DiagnosticOrderReviewListResponseDto,
  DiagnosticPatientReceiveDetailResponseDto,
  DiagnosticPatientReceiveListResponseDto,
  DiagnosticQueueBoardResponseDto,
  DiagnosticQueueEntryDetailResponseDto,
  DiagnosticQueueTurnActionRequest,
  DiagnosticResultEntryDetailResponseDto,
  DiagnosticResultEntryListResponseDto,
  DiagnosticServiceExecutionDetailResponseDto,
  DiagnosticServiceExecutionListResponseDto,
  EnterDiagnosticResultRequest,
  EnterDiagnosticResultResponseDto,
  GetDiagnosticExecutionsParams,
  GetDiagnosticOrdersForReceiveParams,
  GetDiagnosticOrdersForResultEntryParams,
  GetDiagnosticOrdersForReviewParams,
  GetDiagnosticQueueParams,
  ManageDiagnosticQueueActionResponseDto,
  ReceiveDiagnosticPatientRequest,
  ReceiveDiagnosticPatientResponseDto,
  ReceiveDiagnosticPatientsBatchRequest,
  UpdateDiagnosticQueueStatusRequest,
  UpdateDiagnosticServiceStatusBatchRequest,
  UpdateDiagnosticServiceStatusRequest,
  UpdateDiagnosticServiceStatusResponseDto,
} from '../types/diagnostic.types';
import type {
  DiagnosticWorklistItemDto,
  DiagnosticWorklistQueryParams,
  DiagnosticWorklistResponseDto,
} from '../types/diagnostic-worklist.types';

/** Chỉ fallback về mock khi backend chưa mở endpoint (404) hoặc offline/network error. */
function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

/** Worklist endpoint đã có trên BE: chỉ fallback khi offline/network error, không nuốt HTTP 4xx/5xx. */
function shouldFallbackWorklistToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  return !error.response;
}

function hasAccessToken(): boolean {
  return (
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'))
  );
}

/**
 * Lấy dữ liệu tổng quan màn hình khởi đầu của Diagnostic Staff (`GET /diagnostic/dashboard/summary`).
 * Khi chưa có token hoặc backend offline/404, fallback về dữ liệu chuẩn M1;
 * các lỗi phân quyền/nghiệp vụ (400/401/403/500...) được rethrow để UI hiển thị trạng thái lỗi.
 */
export async function fetchDiagnosticDashboardSummary(
  params?: DiagnosticDashboardQueryParams,
): Promise<DiagnosticDashboardSummary> {
  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.get<DiagnosticDashboardSummary>(
        '/diagnostic/dashboard/summary',
        params ? { params } : undefined,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return DIAGNOSTIC_DASHBOARD_MOCK;
}

/**
 * Lấy danh sách công việc cận lâm sàng (`GET /diagnostic/worklist`) cho UC38.
 * Khi chưa có token hoặc offline/network error, fallback về dữ liệu mock;
 * mọi lỗi HTTP (400/401/403/404/500) được rethrow để UI hiển thị cảnh báo chính xác.
 */
export async function fetchDiagnosticWorklist(
  params?: DiagnosticWorklistQueryParams,
): Promise<DiagnosticWorklistResponseDto> {
  if (hasAccessToken()) {
    try {
      const hasParams = Boolean(params && Object.keys(params).length > 0);
      const { data } = await apiClient.get<DiagnosticWorklistResponseDto>(
        '/diagnostic/worklist',
        hasParams ? { params } : undefined,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackWorklistToMock(error)) {
        throw error;
      }
    }
  }

  return resolveMockDiagnosticWorklist(params);
}

/**
 * Lấy chi tiết 1 phiếu chỉ định cận lâm sàng trong danh sách công việc (`GET /diagnostic/worklist/{orderIdOrCode}`).
 */
export async function fetchDiagnosticWorklistOrderDetail(
  orderIdOrCode: string,
): Promise<DiagnosticWorklistItemDto> {
  const parsed = diagnosticOrderIdentifierSchema.safeParse(orderIdOrCode);
  if (!parsed.success) {
    throw new Error(
      parsed.error.issues[0]?.message ??
        'Mã hoặc định danh phiếu chỉ định cận lâm sàng không hợp lệ.',
    );
  }

  const normalizedId = parsed.data;

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.get<DiagnosticWorklistItemDto>(
        `/diagnostic/worklist/${encodeURIComponent(normalizedId)}`,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackWorklistToMock(error)) {
        throw error;
      }
    }
  }

  const item = resolveMockDiagnosticWorklistOrderDetail(normalizedId);
  if (!item) {
    throw new Error('Không tìm thấy phiếu chỉ định cận lâm sàng phù hợp.');
  }

  return item;
}

/**
 * Lấy danh sách phiếu chỉ định cận lâm sàng chờ kiểm tra & xác nhận (`GET /diagnostic/orders/review`).
 */
export async function fetchDiagnosticOrdersForReview(
  params?: GetDiagnosticOrdersForReviewParams,
): Promise<DiagnosticOrderReviewListResponseDto> {
  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.get<DiagnosticOrderReviewListResponseDto>(
        '/diagnostic/orders/review',
        params ? { params } : undefined,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return resolveMockDiagnosticOrdersForReview(params);
}

/**
 * Lấy chi tiết phiếu chỉ định CLS kèm bối cảnh lâm sàng & checklist đủ điều kiện (`GET /diagnostic/orders/{idOrCode}/review`).
 */
export async function fetchDiagnosticOrderReviewDetail(
  idOrCode: string,
): Promise<DiagnosticOrderReviewDetailResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.get<DiagnosticOrderReviewDetailResponseDto>(
        `/diagnostic/orders/${encoded}/review`,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return resolveMockDiagnosticOrderReviewDetail(idOrCode);
}

/**
 * Xác nhận một phiếu chỉ định CLS hợp lệ và xếp vào hàng đợi (`POST /diagnostic/orders/{idOrCode}/confirm`).
 */
export async function confirmDiagnosticOrder(
  idOrCode: string,
  payload?: ConfirmDiagnosticOrderRequest,
): Promise<ConfirmDiagnosticOrderResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<ConfirmDiagnosticOrderResponseDto>(
        `/diagnostic/orders/${encoded}/confirm`,
        payload ?? { isEligibilityVerified: true },
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return confirmMockDiagnosticOrder(idOrCode, payload);
}

/**
 * Xác nhận hàng loạt nhiều phiếu chỉ định CLS hợp lệ (`POST /diagnostic/orders/confirm`).
 */
export async function confirmDiagnosticOrdersBatch(
  payload: ConfirmDiagnosticOrdersBatchRequest,
): Promise<ConfirmDiagnosticOrderResponseDto> {
  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<ConfirmDiagnosticOrderResponseDto>(
        '/diagnostic/orders/confirm',
        payload,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return confirmMockDiagnosticOrdersBatch(payload);
}

/**
 * Lấy bảng hàng đợi cận lâm sàng theo điểm phục vụ & phòng kỹ thuật (`GET /diagnostic/queue`) cho UC41 (#129 / #130).
 */
export async function fetchDiagnosticQueueBoard(
  params?: GetDiagnosticQueueParams,
): Promise<DiagnosticQueueBoardResponseDto> {
  if (hasAccessToken()) {
    try {
      const hasParams = Boolean(params && Object.keys(params).length > 0);
      const { data } = await apiClient.get<DiagnosticQueueBoardResponseDto>(
        '/diagnostic/queue',
        hasParams ? { params } : undefined,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackWorklistToMock(error)) {
        throw error;
      }
    }
  }

  return resolveMockDiagnosticQueueBoard(params);
}

/**
 * Lấy chi tiết một lượt xếp hàng cận lâm sàng & lịch sử chuyển trạng thái (`GET /diagnostic/queue/{idOrCode}`).
 */
export async function fetchDiagnosticQueueEntryDetail(
  idOrCode: string,
): Promise<DiagnosticQueueEntryDetailResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.get<DiagnosticQueueEntryDetailResponseDto>(
        `/diagnostic/queue/${encoded}`,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackWorklistToMock(error)) {
        throw error;
      }
    }
  }

  return resolveMockDiagnosticQueueEntryDetail(idOrCode);
}

/**
 * Gọi bệnh nhân đủ điều kiện tiếp theo tại điểm phục vụ cận lâm sàng (`POST /diagnostic/queue/service-points/{servicePointId}/call-next`).
 */
export async function callNextDiagnosticPatient(
  servicePointId: string,
  payload?: CallNextDiagnosticPatientRequest,
): Promise<CallNextDiagnosticPatientResponseDto> {
  const encoded = encodeURIComponent(servicePointId.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<CallNextDiagnosticPatientResponseDto>(
        `/diagnostic/queue/service-points/${encoded}/call-next`,
        payload ?? {},
      );
      return data;
    } catch (error) {
      if (!shouldFallbackWorklistToMock(error)) {
        throw error;
      }
    }
  }

  return callNextMockDiagnosticPatient(servicePointId, payload);
}

/**
 * Cập nhật trạng thái lượt xếp hàng cận lâm sàng (`POST /diagnostic/queue/{idOrCode}/status`).
 */
export async function updateDiagnosticQueueStatus(
  idOrCode: string,
  payload: UpdateDiagnosticQueueStatusRequest,
): Promise<ManageDiagnosticQueueActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<ManageDiagnosticQueueActionResponseDto>(
        `/diagnostic/queue/${encoded}/status`,
        payload,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackWorklistToMock(error)) {
        throw error;
      }
    }
  }

  return updateMockDiagnosticQueueStatus(idOrCode, payload);
}

/**
 * Đánh dấu bệnh nhân sẵn sàng trong hàng đợi cận lâm sàng (`POST /diagnostic/queue/{idOrCode}/ready`).
 */
export async function markDiagnosticQueueReady(
  idOrCode: string,
  payload?: DiagnosticQueueTurnActionRequest,
): Promise<ManageDiagnosticQueueActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<ManageDiagnosticQueueActionResponseDto>(
        `/diagnostic/queue/${encoded}/ready`,
        payload ?? {},
      );
      return data;
    } catch (error) {
      if (!shouldFallbackWorklistToMock(error)) {
        throw error;
      }
    }
  }

  return updateMockDiagnosticQueueStatus(idOrCode, {
    targetStatus: 'READY',
    presenceStatus: payload?.presenceStatus ?? 'PRESENT',
    reason: payload?.reason,
    notes: payload?.notes,
    expectedStateVersion: payload?.expectedStateVersion,
  });
}

/**
 * Gọi trực tiếp một lượt xếp hàng cận lâm sàng cụ thể (`POST /diagnostic/queue/{idOrCode}/call`).
 */
export async function callDiagnosticQueueEntry(
  idOrCode: string,
  payload?: DiagnosticQueueTurnActionRequest,
): Promise<ManageDiagnosticQueueActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<ManageDiagnosticQueueActionResponseDto>(
        `/diagnostic/queue/${encoded}/call`,
        payload ?? {},
      );
      return data;
    } catch (error) {
      if (!shouldFallbackWorklistToMock(error)) {
        throw error;
      }
    }
  }

  return updateMockDiagnosticQueueStatus(idOrCode, {
    targetStatus: 'CALLED',
    presenceStatus: payload?.presenceStatus ?? 'PRESENT',
    reason: payload?.reason,
    notes: payload?.notes,
    expectedStateVersion: payload?.expectedStateVersion,
  });
}

/**
 * Bắt đầu thực hiện kỹ thuật cận lâm sàng (`POST /diagnostic/queue/{idOrCode}/start`).
 */
export async function startDiagnosticQueueExecution(
  idOrCode: string,
  payload?: DiagnosticQueueTurnActionRequest,
): Promise<ManageDiagnosticQueueActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<ManageDiagnosticQueueActionResponseDto>(
        `/diagnostic/queue/${encoded}/start`,
        payload ?? {},
      );
      return data;
    } catch (error) {
      if (!shouldFallbackWorklistToMock(error)) {
        throw error;
      }
    }
  }

  return updateMockDiagnosticQueueStatus(idOrCode, {
    targetStatus: 'IN_PROGRESS',
    presenceStatus: 'PRESENT',
    reason: payload?.reason,
    notes: payload?.notes,
    expectedStateVersion: payload?.expectedStateVersion,
  });
}

/**
 * Hoàn tất thực hiện lượt xếp hàng cận lâm sàng (`POST /diagnostic/queue/{idOrCode}/complete`).
 */
export async function completeDiagnosticQueueExecution(
  idOrCode: string,
  payload?: DiagnosticQueueTurnActionRequest,
): Promise<ManageDiagnosticQueueActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<ManageDiagnosticQueueActionResponseDto>(
        `/diagnostic/queue/${encoded}/complete`,
        payload ?? {},
      );
      return data;
    } catch (error) {
      if (!shouldFallbackWorklistToMock(error)) {
        throw error;
      }
    }
  }

  return updateMockDiagnosticQueueStatus(idOrCode, {
    targetStatus: 'COMPLETED',
    presenceStatus: payload?.presenceStatus ?? 'PRESENT',
    reason: payload?.reason,
    notes: payload?.notes,
    expectedStateVersion: payload?.expectedStateVersion,
  });
}

/**
 * Đánh dấu bệnh nhân vắng mặt / qua lượt (`POST /diagnostic/queue/{idOrCode}/missed-turn`).
 */
export async function markDiagnosticQueueMissedTurn(
  idOrCode: string,
  payload: DiagnosticQueueTurnActionRequest,
): Promise<ManageDiagnosticQueueActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<ManageDiagnosticQueueActionResponseDto>(
        `/diagnostic/queue/${encoded}/missed-turn`,
        payload,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackWorklistToMock(error)) {
        throw error;
      }
    }
  }

  return markMockDiagnosticMissedTurn(idOrCode, payload);
}

/**
 * Khôi phục lượt chờ cho bệnh nhân qua lượt đã quay lại (`POST /diagnostic/queue/{idOrCode}/restore-turn`).
 */
export async function restoreDiagnosticQueueTurn(
  idOrCode: string,
  payload?: DiagnosticQueueTurnActionRequest,
): Promise<ManageDiagnosticQueueActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<ManageDiagnosticQueueActionResponseDto>(
        `/diagnostic/queue/${encoded}/restore-turn`,
        payload ?? {},
      );
      return data;
    } catch (error) {
      if (!shouldFallbackWorklistToMock(error)) {
        throw error;
      }
    }
  }

  return restoreMockDiagnosticSkippedTurn(idOrCode, payload);
}

/**
 * Hủy lượt xếp hàng cận lâm sàng (`POST /diagnostic/queue/{idOrCode}/cancel`).
 */
export async function cancelDiagnosticQueueEntry(
  idOrCode: string,
  payload: CancelDiagnosticQueueEntryRequest,
): Promise<ManageDiagnosticQueueActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<ManageDiagnosticQueueActionResponseDto>(
        `/diagnostic/queue/${encoded}/cancel`,
        payload,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackWorklistToMock(error)) {
        throw error;
      }
    }
  }

  return cancelMockDiagnosticQueueEntry(idOrCode, payload);
}

/**
 * Lấy danh sách phiếu chỉ định cận lâm sàng chờ tiếp nhận bệnh nhân tại điểm dịch vụ (`GET /diagnostic/orders/receive` — UC40 / #127).
 */
export async function fetchDiagnosticOrdersForReceive(
  params?: GetDiagnosticOrdersForReceiveParams,
): Promise<DiagnosticPatientReceiveListResponseDto> {
  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.get<DiagnosticPatientReceiveListResponseDto>(
        '/diagnostic/orders/receive',
        params ? { params } : undefined,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return resolveMockDiagnosticOrdersForReceive(params);
}

/**
 * Lấy chi tiết phiếu chỉ định CLS kèm bối cảnh lâm sàng & checklist tiếp nhận bệnh nhân (`GET /diagnostic/orders/{idOrCode}/receive` — UC40 / #127).
 */
export async function fetchDiagnosticPatientReceiveDetail(
  idOrCode: string,
): Promise<DiagnosticPatientReceiveDetailResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.get<DiagnosticPatientReceiveDetailResponseDto>(
        `/diagnostic/orders/${encoded}/receive`,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return resolveMockDiagnosticPatientReceiveDetail(idOrCode);
}

/**
 * Tiếp nhận bệnh nhân tại điểm dịch vụ cận lâm sàng, chuyển trạng thái hàng đợi sang READY (`POST /diagnostic/orders/{idOrCode}/receive` — UC40 / #127).
 */
export async function receiveDiagnosticPatient(
  idOrCode: string,
  payload?: ReceiveDiagnosticPatientRequest,
): Promise<ReceiveDiagnosticPatientResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<ReceiveDiagnosticPatientResponseDto>(
        `/diagnostic/orders/${encoded}/receive`,
        payload ?? {
          isPatientPresent: true,
          isIdentityVerified: true,
          isPreparationVerified: true,
        },
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return receiveMockDiagnosticPatient(idOrCode, payload);
}

/**
 * Tiếp nhận bệnh nhân hàng loạt cho các phiếu chỉ định cận lâm sàng đã xác nhận (`POST /diagnostic/orders/receive` — UC40 / #127).
 */
export async function receiveDiagnosticPatientsBatch(
  payload: ReceiveDiagnosticPatientsBatchRequest,
): Promise<ReceiveDiagnosticPatientResponseDto> {
  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<ReceiveDiagnosticPatientResponseDto>(
        '/diagnostic/orders/receive',
        payload,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return receiveMockDiagnosticPatientsBatch(payload);
}

/**
 * Lấy danh sách phiếu chỉ định cận lâm sàng theo trạng thái thực hiện kỹ thuật (`GET /diagnostic/orders/execution` — UC42 / #131).
 */
export async function fetchDiagnosticExecutions(
  params?: GetDiagnosticExecutionsParams,
): Promise<DiagnosticServiceExecutionListResponseDto> {
  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.get<DiagnosticServiceExecutionListResponseDto>(
        '/diagnostic/orders/execution',
        params ? { params } : undefined,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return resolveMockDiagnosticExecutions(params);
}

/**
 * Lấy chi tiết phiếu chỉ định CLS kèm bối cảnh lâm sàng & lịch sử chuyển trạng thái thực hiện (`GET /diagnostic/orders/{idOrCode}/execution` — UC42 / #131).
 */
export async function fetchDiagnosticExecutionDetail(
  idOrCode: string,
): Promise<DiagnosticServiceExecutionDetailResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.get<DiagnosticServiceExecutionDetailResponseDto>(
        `/diagnostic/orders/${encoded}/execution`,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return resolveMockDiagnosticExecutionDetail(idOrCode);
}

/**
 * Cập nhật trạng thái thực hiện kỹ thuật đơn lẻ (`POST /diagnostic/orders/{idOrCode}/execution-status` — UC42 / #131).
 */
export async function updateDiagnosticServiceStatus(
  idOrCode: string,
  payload: UpdateDiagnosticServiceStatusRequest,
): Promise<UpdateDiagnosticServiceStatusResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<UpdateDiagnosticServiceStatusResponseDto>(
        `/diagnostic/orders/${encoded}/execution-status`,
        payload,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return updateMockDiagnosticServiceStatus(idOrCode, payload);
}

/**
 * Bắt đầu thực hiện kỹ thuật (`POST /diagnostic/orders/{idOrCode}/start` — UC42 / #131).
 */
export async function startDiagnosticServiceExecution(
  idOrCode: string,
  payload?: UpdateDiagnosticServiceStatusRequest,
): Promise<UpdateDiagnosticServiceStatusResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<UpdateDiagnosticServiceStatusResponseDto>(
        `/diagnostic/orders/${encoded}/start`,
        payload ?? { targetStatus: 'IN_PROGRESS' },
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return startMockDiagnosticServiceExecution(idOrCode, payload);
}

/**
 * Hoàn tất thực hiện kỹ thuật (`POST /diagnostic/orders/{idOrCode}/complete` — UC42 / #131).
 */
export async function completeDiagnosticServiceExecution(
  idOrCode: string,
  payload?: UpdateDiagnosticServiceStatusRequest,
): Promise<UpdateDiagnosticServiceStatusResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<UpdateDiagnosticServiceStatusResponseDto>(
        `/diagnostic/orders/${encoded}/complete`,
        payload ?? { targetStatus: 'COMPLETED' },
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return completeMockDiagnosticServiceExecution(idOrCode, payload);
}

/**
 * Cập nhật trạng thái thực hiện kỹ thuật hàng loạt (`POST /diagnostic/orders/execution-status` — UC42 / #131).
 */
export async function updateDiagnosticServiceStatusBatch(
  payload: UpdateDiagnosticServiceStatusBatchRequest,
): Promise<UpdateDiagnosticServiceStatusResponseDto> {
  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<UpdateDiagnosticServiceStatusResponseDto>(
        '/diagnostic/orders/execution-status',
        payload,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return updateMockDiagnosticServiceStatusBatch(payload);
}

/**
 * Lấy danh sách phiếu chỉ định cận lâm sàng cần nhập / trả kết quả (`GET /diagnostic/results` — UC43 / #133).
 * Fallback về mock khi backend chưa mở endpoint (404) hoặc offline/network error;
 * các lỗi phân quyền/nghiệp vụ được rethrow để UI hiển thị trạng thái lỗi chính xác.
 */
export async function fetchDiagnosticOrdersForResultEntry(
  params?: GetDiagnosticOrdersForResultEntryParams,
): Promise<DiagnosticResultEntryListResponseDto> {
  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.get<DiagnosticResultEntryListResponseDto>(
        '/diagnostic/results',
        params ? { params } : undefined,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return resolveMockDiagnosticOrdersForResultEntry(params);
}

/**
 * Lấy chi tiết phiếu chỉ định CLS kèm bối cảnh lâm sàng, lịch sử phiên bản kết quả & checklist điều kiện nhập kết quả (`GET /diagnostic/results/{idOrCode}` — UC43 / #133).
 */
export async function fetchDiagnosticResultEntryDetail(
  idOrCode: string,
): Promise<DiagnosticResultEntryDetailResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.get<DiagnosticResultEntryDetailResponseDto>(
        `/diagnostic/results/${encoded}`,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return resolveMockDiagnosticResultEntryDetail(idOrCode);
}

/**
 * Nhân viên cận lâm sàng lưu / duyệt & trả kết quả cho phiếu chỉ định (`POST /diagnostic/orders/{idOrCode}/result` — UC43 / #133).
 * Mutation không nuốt lỗi nghiệp vụ (400/403/409): chỉ fallback về mock khi backend chưa mở endpoint hoặc offline.
 */
export async function enterDiagnosticResult(
  idOrCode: string,
  payload: EnterDiagnosticResultRequest,
): Promise<EnterDiagnosticResultResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());

  if (hasAccessToken()) {
    try {
      const { data } = await apiClient.post<EnterDiagnosticResultResponseDto>(
        `/diagnostic/orders/${encoded}/result`,
        payload,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return enterMockDiagnosticResult(idOrCode, payload);
}
