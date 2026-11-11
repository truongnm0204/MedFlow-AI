import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { DIAGNOSTIC_DASHBOARD_MOCK } from '../data/diagnostic-mock-data';
import {
  confirmMockDiagnosticOrder,
  confirmMockDiagnosticOrdersBatch,
  resolveMockDiagnosticOrderReviewDetail,
  resolveMockDiagnosticOrdersForReview,
} from '../data/diagnostic-order-review-mock';
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

/** Chỉ fallback về mock khi backend chưa mở endpoint (404) hoặc offline/network error. */
function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

/**
 * Lấy dữ liệu tổng quan màn hình khởi đầu của Diagnostic Staff (`GET /diagnostic/dashboard/summary`).
 * Khi chưa có token hoặc backend offline/404, fallback về dữ liệu chuẩn M1;
 * các lỗi phân quyền/nghiệp vụ (400/401/403/500...) được rethrow để UI hiển thị trạng thái lỗi.
 */
export async function fetchDiagnosticDashboardSummary(
  params?: DiagnosticDashboardQueryParams,
): Promise<DiagnosticDashboardSummary> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
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

function hasAccessToken(): boolean {
  return (
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'))
  );
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
