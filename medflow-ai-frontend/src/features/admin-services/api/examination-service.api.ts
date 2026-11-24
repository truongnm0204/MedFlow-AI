import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import type { ExaminationServiceFormValues } from '../schemas/examination-service.schema';
import type {
  ExaminationService,
  PriceHistoryEntry,
  ServiceCategory,
  UpdateServicePricePayload,
} from '../types/examination-service.types';

export interface ServicePriceHistoryApiEntryDto {
  id?: number;
  date?: string | null;
  validFrom?: string | null;
  validTo?: string | null;
  price?: number | null;
  priceAmount?: number | null;
  decisionNumber?: string | null;
  note?: string | null;
  appliedBy?: string | null;
  isActive?: boolean;
  status?: string | null;
}

export interface ExaminationServiceApiItemDto {
  id: number | string;
  serviceCode?: string;
  code?: string;
  name: string;
  description?: string | null;
  serviceType?: string | null;
  category?: string | null;
  hospitalId?: number | null;
  facility?: string | null;
  departmentId?: number | null;
  departmentCode?: string | null;
  departmentName?: string | null;
  department?: string | null;
  price?: number | null;
  currentPriceAmount?: number | null;
  hasEffectivePrice?: boolean | null;
  priceFormatted?: string | null;
  allowsOnlineBooking?: boolean;
  onlineBooking?: boolean;
  isActive?: boolean;
  status?: string | null;
  hasActiveBookings?: boolean | null;
  activeBookingsCount?: number | null;
  validFrom?: string | null;
  validTo?: string | null;
  effectiveDate?: string | null;
  decisionNumber?: string | null;
  appliedBy?: string | null;
  note?: string | null;
  priceHistory?: ServicePriceHistoryApiEntryDto[];
}

interface ExaminationServiceAdminListResponseDto {
  data?: ExaminationServiceApiItemDto[];
  total?: number;
}

export interface ServicePriceDetailResponseDto {
  serviceId: number;
  serviceCode: string;
  name: string;
  serviceType: string;
  category: string;
  hospitalId?: number | null;
  facility: string;
  departmentId?: number | null;
  departmentName?: string | null;
  isActive: boolean;
  status: string;
  price: number;
  currentPriceAmount?: number | null;
  hasEffectivePrice: boolean;
  priceFormatted: string;
  validFrom?: string | null;
  validTo?: string | null;
  effectiveDate?: string | null;
  decisionNumber?: string | null;
  appliedBy?: string | null;
  note?: string | null;
  priceHistory?: ServicePriceHistoryApiEntryDto[];
}

interface ProblemDetailsBody {
  detail?: string;
  title?: string;
  message?: string;
}

function extractServiceApiError(error: unknown, fallbackMessage: string): Error {
  if (axios.isAxiosError(error)) {
    const body = error.response?.data as ProblemDetailsBody | undefined;
    const msg = body?.detail || body?.message || body?.title;
    if (msg && msg.trim().length > 0) return new Error(msg.trim());
  }
  if (error instanceof Error) return error;
  return new Error(fallbackMessage);
}

function normalizeServiceCategory(
  rawCategory?: string | null,
  rawType?: string | null,
): ServiceCategory {
  if (
    rawCategory === 'Khám Lâm sàng' ||
    rawCategory === 'Hội chẩn Lâm sàng' ||
    rawCategory === 'Cận lâm sàng kỹ thuật'
  ) {
    return rawCategory;
  }
  const upper = (rawType ?? rawCategory ?? '').toUpperCase();
  if (upper === 'CONSULTATION') return 'Hội chẩn Lâm sàng';
  if (upper === 'DIAGNOSTIC') return 'Cận lâm sàng kỹ thuật';
  return 'Khám Lâm sàng';
}

function mapPriceHistoryEntryDto(entry: ServicePriceHistoryApiEntryDto): PriceHistoryEntry {
  const dateText =
    entry.date || (entry.validFrom ? entry.validFrom.slice(0, 10) : null) || 'Chưa cập nhật';

  return {
    id: entry.id,
    date: dateText,
    validFrom: entry.validFrom ?? null,
    validTo: entry.validTo ? entry.validTo.slice(0, 10) : null,
    price: entry.price ?? entry.priceAmount ?? 0,
    decisionNumber: entry.decisionNumber || 'Chưa cập nhật',
    note: entry.note || 'Biểu phí dịch vụ tại cơ sở',
    appliedBy: entry.appliedBy || 'Chưa cập nhật',
    isActive: entry.isActive,
    status: entry.status ?? undefined,
  };
}

export function mapExaminationServiceDto(dto: ExaminationServiceApiItemDto): ExaminationService {
  const code = dto.serviceCode || dto.code || String(dto.id);
  const statusUpper = (dto.status ?? '').toUpperCase();
  const isSuspended =
    statusUpper === 'SUSPENDED' || statusUpper === 'INACTIVE' || dto.isActive === false;
  const resolvedPrice = dto.currentPriceAmount ?? dto.price ?? 0;
  const effectiveDate =
    dto.effectiveDate || (dto.validFrom ? dto.validFrom.slice(0, 10) : undefined);

  return {
    id: String(dto.id),
    code,
    name: dto.name,
    description: dto.description ?? '',
    category: normalizeServiceCategory(dto.category, dto.serviceType),
    department: dto.departmentName || dto.department || 'Chưa phân khoa',
    price: resolvedPrice,
    hasEffectivePrice: dto.hasEffectivePrice ?? (dto.price != null && dto.price >= 0),
    onlineBooking: dto.allowsOnlineBooking ?? dto.onlineBooking ?? false,
    status: isSuspended ? 'SUSPENDED' : 'ACTIVE',
    effectiveDate: effectiveDate ?? undefined,
    validTo: dto.validTo ? dto.validTo.slice(0, 10) : undefined,
    decisionNumber: dto.decisionNumber ?? undefined,
    appliedBy: dto.appliedBy ?? undefined,
    priceNote: dto.note ?? undefined,
    priceHistory: dto.priceHistory ? dto.priceHistory.map(mapPriceHistoryEntryDto) : undefined,
    hasActiveBookings: dto.hasActiveBookings ?? (dto.activeBookingsCount ?? 0) > 0,
    activeBookingsCount: dto.activeBookingsCount ?? 0,
  };
}

/**
 * Lấy danh sách dịch vụ khám từ backend API `GET /api/v1/admin/examination-services`.
 */
export async function fetchExaminationServicesApi(): Promise<ExaminationService[]> {
  try {
    const { data } = await apiClient.get<
      ExaminationServiceAdminListResponseDto | ExaminationServiceApiItemDto[]
    >('/admin/examination-services', {
      params: { page: 1, pageSize: 100 },
    });
    const raw = Array.isArray(data) ? data : (data.data ?? []);
    return raw.map(mapExaminationServiceDto);
  } catch (error) {
    throw extractServiceApiError(error, 'Không thể tải danh mục dịch vụ khám.');
  }
}

/**
 * Lấy danh sách biểu phí hiện hành & lịch sử giá theo cơ sở (`GET /api/v1/admin/service-prices`).
 */
export async function fetchAdminServicePricesApi(): Promise<ExaminationService[]> {
  try {
    const { data } = await apiClient.get<
      ExaminationServiceAdminListResponseDto | ExaminationServiceApiItemDto[]
    >('/admin/service-prices', {
      params: { page: 1, pageSize: 200 },
    });
    const raw = Array.isArray(data) ? data : (data.data ?? []);
    return raw.map(mapExaminationServiceDto);
  } catch (error) {
    throw extractServiceApiError(error, 'Không thể tải bảng giá dịch vụ của cơ sở y tế.');
  }
}

/**
 * Lấy lịch sử thay đổi giá chi tiết của một dịch vụ (`GET /api/v1/admin/examination-services/{idOrCode}/prices`).
 */
export async function fetchServicePriceHistoryApi(idOrCode: string): Promise<PriceHistoryEntry[]> {
  try {
    const { data } = await apiClient.get<ServicePriceDetailResponseDto>(
      `/admin/examination-services/${encodeURIComponent(idOrCode)}/prices`,
    );
    return (data.priceHistory ?? []).map(mapPriceHistoryEntryDto);
  } catch (error) {
    throw extractServiceApiError(error, 'Không thể tải lịch sử biểu phí dịch vụ.');
  }
}

export async function createExaminationServiceApi(
  values: ExaminationServiceFormValues,
): Promise<ExaminationService> {
  try {
    const { data } = await apiClient.post<ExaminationServiceApiItemDto>(
      '/admin/examination-services',
      {
        serviceCode: values.code,
        code: values.code,
        name: values.name,
        category: values.category,
        department: values.department,
        description: values.description ?? '',
        allowsOnlineBooking: values.onlineBooking,
        onlineBooking: values.onlineBooking,
        status: values.status,
        isActive: values.status === 'ACTIVE',
      },
    );
    return mapExaminationServiceDto(data);
  } catch (error) {
    throw extractServiceApiError(error, 'Không thể tạo mới dịch vụ khám.');
  }
}

export async function updateExaminationServiceApi(
  serviceId: string,
  values: Partial<ExaminationServiceFormValues> & { price?: number },
): Promise<ExaminationService> {
  const numericId = serviceId.replace(/\D/g, '') || serviceId;
  try {
    const { data } = await apiClient.put<ExaminationServiceApiItemDto>(
      `/admin/examination-services/${encodeURIComponent(numericId)}`,
      {
        code: values.code,
        serviceCode: values.code,
        name: values.name,
        category: values.category,
        department: values.department,
        description: values.description,
        price: values.price,
        allowsOnlineBooking: values.onlineBooking,
        onlineBooking: values.onlineBooking,
        status: values.status,
        isActive: values.status ? values.status === 'ACTIVE' : undefined,
      },
    );
    return mapExaminationServiceDto(data);
  } catch (error) {
    throw extractServiceApiError(error, 'Không thể cập nhật thông tin dịch vụ khám.');
  }
}

export async function deactivateExaminationServiceApi(
  idOrCode: string,
  reason?: string,
): Promise<void> {
  try {
    await apiClient.patch(
      `/admin/examination-services/${encodeURIComponent(idOrCode)}/deactivate`,
      { reason: reason ?? 'Tạm ngừng dịch vụ từ cổng quản trị' },
    );
  } catch (error) {
    throw extractServiceApiError(error, 'Không thể tạm ngừng dịch vụ khám.');
  }
}

export async function reactivateExaminationServiceApi(idOrCode: string): Promise<void> {
  try {
    await apiClient.patch(
      `/admin/examination-services/${encodeURIComponent(idOrCode)}/reactivate`,
      {},
    );
  } catch (error) {
    throw extractServiceApiError(error, 'Không thể kích hoạt lại dịch vụ khám.');
  }
}

/**
 * Cập nhật đơn giá dịch vụ theo chính sách bệnh viện (UC52 / #208).
 * Gọi API thật `PUT /api/v1/admin/examination-services/{idOrCode}/prices`.
 */
export async function updateServicePriceApi(
  idOrCode: string,
  payload: UpdateServicePricePayload,
): Promise<ExaminationService> {
  const trimmedTarget = idOrCode.trim();
  try {
    const { data } = await apiClient.put<ExaminationServiceApiItemDto>(
      `/admin/examination-services/${encodeURIComponent(trimmedTarget)}/prices`,
      {
        priceAmount: payload.price,
        price: payload.price,
        validFrom: payload.effectiveDate,
        effectiveDate: payload.effectiveDate,
        validTo: payload.validTo?.trim() ? payload.validTo.trim() : null,
        decisionNumber: payload.decisionNumber,
        appliedBy: payload.appliedBy,
        note: payload.note,
      },
    );
    return mapExaminationServiceDto(data);
  } catch (error) {
    throw extractServiceApiError(error, 'Có lỗi xảy ra khi cập nhật giá dịch vụ.');
  }
}
