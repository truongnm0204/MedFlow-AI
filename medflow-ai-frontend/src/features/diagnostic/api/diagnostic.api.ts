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
  resolveMockDiagnosticWorklist,
  resolveMockDiagnosticWorklistOrderDetail,
} from '../data/diagnostic-worklist-mock';
import { diagnosticOrderIdentifierSchema } from '../schemas/diagnostic-worklist-filter.schema';
import type {
  ConfirmDiagnosticOrderRequest,
  ConfirmDiagnosticOrderResponseDto,
  ConfirmDiagnosticOrdersBatchRequest,
  DiagnosticDashboardQueryParams,
  DiagnosticDashboardSummary,
  DiagnosticOrderReviewDetailResponseDto,
  DiagnosticOrderReviewListResponseDto,
  GetDiagnosticOrdersForReviewParams,
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
