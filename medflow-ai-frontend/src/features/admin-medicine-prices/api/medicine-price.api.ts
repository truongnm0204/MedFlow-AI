import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import type {
  MedicinePriceDetail,
  MedicinePriceHistoryEntry,
  MedicinePriceItem,
  MedicinePriceListParams,
  MedicinePriceListResult,
  MedicinePricingMetrics,
  UpdateMedicinePricePayload,
} from '../types/medicine-price.types';

export interface MedicinePriceHistoryEntryDto {
  id?: number;
  priceAmount?: number | null;
  price?: number | null;
  priceFormatted?: string | null;
  validFrom?: string | null;
  validTo?: string | null;
  date?: string | null;
  decisionNumber?: string | null;
  note?: string | null;
  appliedBy?: string | null;
  isActive?: boolean | null;
  status?: string | null;
}

export interface MedicinePriceApiItemDto {
  id: number;
  medicineCode?: string | null;
  name: string;
  activeIngredient?: string | null;
  strength?: string | null;
  dosageForm?: string | null;
  dispensingUnit?: string | null;
  hospitalId?: number | null;
  facility?: string | null;
  isActive?: boolean | null;
  status?: string | null;
  price?: number | null;
  currentPriceAmount?: number | null;
  hasEffectivePrice?: boolean | null;
  priceFormatted?: string | null;
  validFrom?: string | null;
  validTo?: string | null;
  effectiveDate?: string | null;
  decisionNumber?: string | null;
  appliedBy?: string | null;
  note?: string | null;
  priceHistory?: MedicinePriceHistoryEntryDto[];
}

export interface MedicinePriceAdminListResponseDto {
  hospitalId?: number | null;
  hospitalName?: string | null;
  data?: MedicinePriceApiItemDto[];
  total?: number;
  page?: number;
  pageSize?: number;
  totalPages?: number;
  metrics?: Partial<MedicinePricingMetrics> | null;
}

export interface MedicinePriceDetailResponseDto extends MedicinePriceApiItemDto {
  medicineId: number;
  medicineCode?: string | null;
}

export interface MedicinePriceHistoryResponseDto {
  medicineId: number;
  medicineCode?: string | null;
  name: string;
  hospitalId?: number | null;
  priceHistory?: MedicinePriceHistoryEntryDto[];
}

interface ProblemDetailsBody {
  detail?: string;
  title?: string;
  message?: string;
}

const EMPTY_METRICS: MedicinePricingMetrics = {
  totalMedicines: 0,
  pricedMedicinesCount: 0,
  pricedRatioPercent: 0,
  averagePrice: 0,
  minPrice: 0,
  maxPrice: 0,
  latestEffectiveDate: null,
};

function formatVnd(amount: number): string {
  return `${amount.toLocaleString('vi-VN')} đ`;
}

export function extractMedicinePriceApiError(error: unknown, fallbackMessage: string): Error {
  if (axios.isAxiosError(error)) {
    const body = error.response?.data as ProblemDetailsBody | undefined;
    const msg = body?.detail || body?.message || body?.title;
    if (msg && msg.trim().length > 0) return new Error(msg.trim());
  }
  if (error instanceof Error) return error;
  return new Error(fallbackMessage);
}

export function mapMedicinePriceHistoryEntryDto(
  entry: MedicinePriceHistoryEntryDto,
): MedicinePriceHistoryEntry {
  const priceAmount = entry.priceAmount ?? entry.price ?? 0;
  const isActive = entry.isActive ?? false;

  return {
    id: entry.id ?? 0,
    priceAmount,
    priceFormatted: entry.priceFormatted || formatVnd(priceAmount),
    validFrom: entry.validFrom ?? null,
    validTo: entry.validTo ? entry.validTo.slice(0, 10) : null,
    date: entry.date || (entry.validFrom ? entry.validFrom.slice(0, 10) : 'Chưa cập nhật'),
    decisionNumber: entry.decisionNumber ?? null,
    note: entry.note ?? null,
    appliedBy: entry.appliedBy ?? null,
    isActive,
    status: entry.status || (isActive ? 'ACTIVE' : 'INACTIVE'),
  };
}

export function mapMedicinePriceDto(dto: MedicinePriceApiItemDto): MedicinePriceItem {
  const resolvedPrice = dto.currentPriceAmount ?? dto.price ?? 0;
  const effectiveDate = dto.effectiveDate ?? (dto.validFrom ? dto.validFrom.slice(0, 10) : null);

  return {
    id: dto.id,
    medicineCode: dto.medicineCode ?? null,
    name: dto.name,
    activeIngredient: dto.activeIngredient ?? null,
    strength: dto.strength ?? null,
    dosageForm: dto.dosageForm ?? null,
    dispensingUnit: dto.dispensingUnit ?? null,
    hospitalId: dto.hospitalId ?? null,
    facility: dto.facility ?? 'Chưa cập nhật',
    isActive: dto.isActive ?? (dto.status ?? '').toUpperCase() !== 'INACTIVE',
    status: dto.status ?? (dto.isActive === false ? 'INACTIVE' : 'ACTIVE'),
    price: resolvedPrice,
    currentPriceAmount: dto.currentPriceAmount ?? null,
    hasEffectivePrice: dto.hasEffectivePrice ?? dto.currentPriceAmount != null,
    priceFormatted: dto.priceFormatted || formatVnd(resolvedPrice),
    validFrom: dto.validFrom ? dto.validFrom.slice(0, 10) : null,
    validTo: dto.validTo ? dto.validTo.slice(0, 10) : null,
    effectiveDate,
    decisionNumber: dto.decisionNumber ?? null,
    appliedBy: dto.appliedBy ?? null,
    note: dto.note ?? null,
    priceHistory: (dto.priceHistory ?? []).map(mapMedicinePriceHistoryEntryDto),
  };
}

function computeMetrics(items: MedicinePriceItem[]): MedicinePricingMetrics {
  if (items.length === 0) return { ...EMPTY_METRICS };

  const priced = items.filter((item) => item.hasEffectivePrice);
  const prices = priced.map((item) => item.price);
  const latestEffectiveDate = items
    .map((item) => item.effectiveDate ?? item.validFrom)
    .filter((value): value is string => Boolean(value))
    .sort()
    .at(-1);

  return {
    totalMedicines: items.length,
    pricedMedicinesCount: priced.length,
    pricedRatioPercent: Math.round((priced.length / items.length) * 100),
    averagePrice:
      prices.length > 0
        ? Math.round(prices.reduce((sum, price) => sum + price, 0) / prices.length)
        : 0,
    minPrice: prices.length > 0 ? Math.min(...prices) : 0,
    maxPrice: prices.length > 0 ? Math.max(...prices) : 0,
    latestEffectiveDate: latestEffectiveDate ?? null,
  };
}

function mapMetrics(
  rawMetrics: Partial<MedicinePricingMetrics> | null | undefined,
  items: MedicinePriceItem[],
): MedicinePricingMetrics {
  const fallback = computeMetrics(items);
  if (!rawMetrics) return fallback;
  return {
    totalMedicines: rawMetrics.totalMedicines ?? fallback.totalMedicines,
    pricedMedicinesCount: rawMetrics.pricedMedicinesCount ?? fallback.pricedMedicinesCount,
    pricedRatioPercent: rawMetrics.pricedRatioPercent ?? fallback.pricedRatioPercent,
    averagePrice: rawMetrics.averagePrice ?? fallback.averagePrice,
    minPrice: rawMetrics.minPrice ?? fallback.minPrice,
    maxPrice: rawMetrics.maxPrice ?? fallback.maxPrice,
    latestEffectiveDate: rawMetrics.latestEffectiveDate ?? fallback.latestEffectiveDate,
  };
}

function buildListParams(
  params?: MedicinePriceListParams,
): Record<string, string | number | boolean> {
  const query: Record<string, string | number | boolean> = {
    page: params?.page ?? 1,
    pageSize: params?.pageSize ?? 200,
  };
  if (params?.keyword?.trim()) query.keyword = params.keyword.trim();
  if (params?.medicineId != null) query.medicineId = params.medicineId;
  if (params?.hospitalId != null) query.hospitalId = params.hospitalId;
  if (params?.isActive != null) query.isActive = params.isActive;
  if (params?.hasEffectivePrice != null) query.hasEffectivePrice = params.hasEffectivePrice;
  if (params?.effectiveAt?.trim()) query.effectiveAt = params.effectiveAt.trim();
  return query;
}

/**
 * Lấy danh sách thuốc kèm giá hiện hành (`GET /api/v1/admin/medicine-prices`).
 */
export async function fetchAdminMedicinePricesApi(
  params?: MedicinePriceListParams,
): Promise<MedicinePriceListResult> {
  try {
    const { data } = await apiClient.get<MedicinePriceAdminListResponseDto>(
      '/admin/medicine-prices',
      { params: buildListParams(params) },
    );
    const items = (data.data ?? []).map(mapMedicinePriceDto);
    return {
      hospitalId: data.hospitalId ?? null,
      hospitalName: data.hospitalName ?? null,
      data: items,
      total: data.total ?? items.length,
      page: data.page ?? params?.page ?? 1,
      pageSize: data.pageSize ?? params?.pageSize ?? 200,
      totalPages: data.totalPages ?? 1,
      metrics: mapMetrics(data.metrics, items),
    };
  } catch (error) {
    throw extractMedicinePriceApiError(error, 'Không thể tải bảng giá thuốc của cơ sở y tế.');
  }
}

/**
 * Lấy lịch sử giá của một thuốc (`GET /api/v1/admin/medicines/{idOrCode}/prices/history`).
 * Nếu phản hồi không kèm lịch sử, tự động đọc lại từ chi tiết giá thuốc.
 */
export async function fetchMedicinePriceHistoryApi(
  idOrCode: string,
  hospitalId?: number,
): Promise<MedicinePriceHistoryEntry[]> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const params = hospitalId != null ? { hospitalId } : undefined;
  try {
    const { data } = await apiClient.get<
      MedicinePriceHistoryResponseDto | MedicinePriceDetailResponseDto
    >(`/admin/medicines/${encoded}/prices/history`, { params });
    const entries = (data.priceHistory ?? []).map(mapMedicinePriceHistoryEntryDto);
    if (entries.length > 0) return entries;
  } catch (error) {
    const fallbackError = extractMedicinePriceApiError(error, 'Không thể tải lịch sử giá thuốc.');
    if (!axios.isAxiosError(error) || !error.response) throw fallbackError;
  }

  try {
    const { data } = await apiClient.get<MedicinePriceDetailResponseDto>(
      `/admin/medicines/${encoded}/prices`,
      { params },
    );
    return (data.priceHistory ?? []).map(mapMedicinePriceHistoryEntryDto);
  } catch (error) {
    throw extractMedicinePriceApiError(error, 'Không thể tải lịch sử giá thuốc.');
  }
}

/**
 * Lấy chi tiết giá thuốc (`GET /api/v1/admin/medicines/{idOrCode}/prices`).
 */
export async function fetchMedicinePriceDetailApi(
  idOrCode: string,
  hospitalId?: number,
): Promise<MedicinePriceDetail> {
  try {
    const { data } = await apiClient.get<MedicinePriceDetailResponseDto>(
      `/admin/medicines/${encodeURIComponent(idOrCode.trim())}/prices`,
      { params: hospitalId != null ? { hospitalId } : undefined },
    );
    return {
      ...mapMedicinePriceDto({ ...data, id: data.medicineId }),
      medicineId: data.medicineId,
      medicineCode: data.medicineCode ?? null,
    };
  } catch (error) {
    throw extractMedicinePriceApiError(error, 'Không thể tải chi tiết giá thuốc.');
  }
}

/**
 * Cập nhật giá thuốc theo chính sách bệnh viện.
 * Gọi API thật `PUT /api/v1/admin/medicines/{idOrCode}/prices`.
 */
export async function updateMedicinePriceApi(
  idOrCode: string,
  payload: UpdateMedicinePricePayload,
): Promise<MedicinePriceItem> {
  const resolvedPrice = payload.priceAmount ?? payload.price ?? 0;
  const effectiveDate = payload.effectiveDate ?? payload.validFrom ?? null;
  const body: Record<string, unknown> = {
    priceAmount: resolvedPrice,
    price: resolvedPrice,
    validFrom: effectiveDate,
    effectiveDate,
    validTo: payload.validTo?.trim() ? payload.validTo.trim() : null,
    decisionNumber: payload.decisionNumber ?? null,
    appliedBy: payload.appliedBy ?? null,
    note: payload.note ?? null,
  };
  if (payload.medicineId != null) body.medicineId = payload.medicineId;
  if (payload.medicineCode) body.medicineCode = payload.medicineCode;
  if (payload.reason?.trim()) body.reason = payload.reason.trim();

  try {
    const { data } = await apiClient.put<MedicinePriceApiItemDto>(
      `/admin/medicines/${encodeURIComponent(idOrCode.trim())}/prices`,
      body,
    );
    return mapMedicinePriceDto(data);
  } catch (error) {
    throw extractMedicinePriceApiError(error, 'Có lỗi xảy ra khi cập nhật giá thuốc.');
  }
}
