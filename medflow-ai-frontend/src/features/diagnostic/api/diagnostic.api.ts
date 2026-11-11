import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { DIAGNOSTIC_DASHBOARD_MOCK } from '../data/diagnostic-mock-data';
import {
  resolveMockDiagnosticWorklist,
  resolveMockDiagnosticWorklistOrderDetail,
} from '../data/diagnostic-worklist-mock';
import { diagnosticOrderIdentifierSchema } from '../schemas/diagnostic-worklist-filter.schema';
import type {
  DiagnosticDashboardQueryParams,
  DiagnosticDashboardSummary,
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

/**
 * Lấy danh sách công việc cận lâm sàng (`GET /diagnostic/worklist`) cho UC39.
 * Khi chưa có token hoặc offline/network error, fallback về dữ liệu mock;
 * mọi lỗi HTTP (400/401/403/404/500) được rethrow để UI hiển thị cảnh báo chính xác.
 */
export async function fetchDiagnosticWorklist(
  params?: DiagnosticWorklistQueryParams,
): Promise<DiagnosticWorklistResponseDto> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
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
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
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
