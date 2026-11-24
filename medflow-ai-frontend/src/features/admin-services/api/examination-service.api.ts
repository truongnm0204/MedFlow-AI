import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import type { ExaminationServiceFormValues } from '../schemas/examination-service.schema';
import type {
  ExaminationService,
  PriceHistoryEntry,
  ServiceCategory,
  UpdateServicePricePayload,
} from '../types/examination-service.types';

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
  priceFormatted?: string | null;
  allowsOnlineBooking?: boolean;
  onlineBooking?: boolean;
  isActive?: boolean;
  status?: string | null;
  hasActiveBookings?: boolean | null;
  activeBookingsCount?: number | null;
  effectiveDate?: string | null;
  decisionNumber?: string | null;
  priceHistory?: PriceHistoryEntry[];
}

interface ExaminationServiceAdminListResponseDto {
  data?: ExaminationServiceApiItemDto[];
  total?: number;
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

export function mapExaminationServiceDto(dto: ExaminationServiceApiItemDto): ExaminationService {
  const code = dto.serviceCode || dto.code || String(dto.id);
  const statusUpper = (dto.status ?? '').toUpperCase();
  const isSuspended =
    statusUpper === 'SUSPENDED' || statusUpper === 'INACTIVE' || dto.isActive === false;

  return {
    id: String(dto.id),
    code,
    name: dto.name,
    description: dto.description ?? '',
    category: normalizeServiceCategory(dto.category, dto.serviceType),
    department: dto.departmentName || dto.department || 'Chưa phân khoa',
    price: dto.price ?? 0,
    onlineBooking: dto.allowsOnlineBooking ?? dto.onlineBooking ?? false,
    status: isSuspended ? 'SUSPENDED' : 'ACTIVE',
    effectiveDate: dto.effectiveDate ?? undefined,
    decisionNumber: dto.decisionNumber ?? undefined,
    priceHistory: dto.priceHistory,
    hasActiveBookings: dto.hasActiveBookings ?? (dto.activeBookingsCount ?? 0) > 0,
    activeBookingsCount: dto.activeBookingsCount ?? 0,
  };
}

/**
 * Lấy danh sách dịch vụ khám và thông tin giá từ backend API `GET /api/v1/admin/examination-services`.
 */
export async function fetchExaminationServicesApi(): Promise<ExaminationService[]> {
  try {
    const { data } = await apiClient.get<
      ExaminationServiceAdminListResponseDto | ExaminationServiceApiItemDto[]
    >('/admin/examination-services', {
      params: { page: 1, pageSize: 200 },
    });
    const raw = Array.isArray(data) ? data : (data.data ?? []);
    return raw.map(mapExaminationServiceDto);
  } catch (error) {
    throw extractServiceApiError(error, 'Không thể tải danh mục dịch vụ khám.');
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
 * Cập nhật đơn giá dịch vụ khám theo chính sách bệnh viện (UC52).
 * Gọi API `PUT /api/v1/admin/examination-services/{id}` với payload `{ price: payload.price }`.
 */
export async function updateServicePriceApi(
  serviceId: string,
  payload: UpdateServicePricePayload,
): Promise<ExaminationService> {
  const numericId = serviceId.replace(/\D/g, '') || serviceId;
  try {
    const { data } = await apiClient.put<ExaminationServiceApiItemDto>(
      `/admin/examination-services/${encodeURIComponent(numericId)}`,
      {
        price: payload.price,
      },
    );
    const mapped = mapExaminationServiceDto(data);
    return {
      ...mapped,
      price: mapped.price || payload.price,
      effectiveDate: payload.effectiveDate,
      decisionNumber: payload.decisionNumber,
      priceHistory: [
        {
          date: payload.effectiveDate,
          price: payload.price,
          decisionNumber: payload.decisionNumber,
          note: payload.note || `Điều chỉnh giá theo QĐ ${payload.decisionNumber}`,
          appliedBy: payload.appliedBy,
        },
        ...(mapped.priceHistory ?? []),
      ],
    };
  } catch (error) {
    throw extractServiceApiError(error, 'Có lỗi xảy ra khi cập nhật giá dịch vụ.');
  }
}
