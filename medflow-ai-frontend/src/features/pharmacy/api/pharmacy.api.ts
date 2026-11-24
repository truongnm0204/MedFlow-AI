import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { PHARMACY_DASHBOARD_MOCK } from '../data/pharmacy-mock-data';
import type {
  ConfirmDispensePayload,
  DispenseConfirmContext,
} from '../types/dispense-confirm.types';
import type { DispensingResultDetail } from '../types/dispensing-result.types';
import type {
  PharmacyDashboardQueryParams,
  PharmacyDashboardSummary,
} from '../types/pharmacy.types';
import type {
  PreparePrescriptionContext,
  PreparePrescriptionPayload,
  PreparePrescriptionResponse,
} from '../types/prepare-medicine.types';
import type {
  PrescriptionSearchItem,
  PrescriptionSearchMetrics,
  PrescriptionSearchStatus,
} from '../types/prescription-search.types';

interface ProblemDetailsBody {
  detail?: string;
  title?: string;
  message?: string;
}

function extractPharmacyApiError(error: unknown, fallbackMessage: string): Error {
  if (axios.isAxiosError(error)) {
    const body = error.response?.data as ProblemDetailsBody | undefined;
    const msg = body?.detail || body?.message || body?.title;
    if (msg && msg.trim().length > 0) {
      return new Error(msg.trim());
    }
  }
  if (error instanceof Error) {
    return error;
  }
  return new Error(fallbackMessage);
}

/** Chỉ dùng cho dashboard M1 khi backend chưa mở endpoint (404) hoặc offline. */
function shouldFallbackDashboardToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

/**
 * Lấy dữ liệu tổng quan màn hình khởi đầu của Dược sĩ (`GET /pharmacy/dashboard/summary`).
 */
export async function fetchPharmacyDashboardSummary(
  params?: PharmacyDashboardQueryParams,
): Promise<PharmacyDashboardSummary> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.get<PharmacyDashboardSummary>(
        '/pharmacy/dashboard/summary',
        params ? { params } : undefined,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackDashboardToMock(error)) {
        throw error;
      }
    }
  }

  return PHARMACY_DASHBOARD_MOCK;
}

export interface PrescriptionSearchApiItemDto {
  id: number;
  prescriptionCode: string;
  visitCode: string;
  patientCode: string;
  patientName: string;
  gender: string | null;
  dateOfBirth: string | null;
  doctorName: string | null;
  status: string;
  issuedAt: string | null;
  itemCount: number;
}

export interface PrescriptionSearchApiResponseDto {
  items: PrescriptionSearchApiItemDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface PrescriptionSearchQueryParams {
  query?: string;
  status?: string;
  page?: number;
  pageSize?: number;
}

export interface PrescriptionSearchResult {
  items: PrescriptionSearchItem[];
  totalCount: number;
  metrics: PrescriptionSearchMetrics;
}

function normalizePrescriptionStatus(status: string): PrescriptionSearchStatus {
  const upper = status?.trim().toUpperCase() || '';
  if (upper === 'PREPARING' || upper === 'IN_PROGRESS') {
    return 'PREPARING';
  }
  if (upper === 'PREPARED' || upper === 'READY' || upper === 'READY_FOR_DISPENSE') {
    return 'READY_FOR_DISPENSE';
  }
  if (upper === 'DISPENSED' || upper === 'COMPLETED') {
    return 'COMPLETED';
  }
  return 'PENDING_APPROVAL';
}

function mapGenderLabel(gender: string | null): string {
  if (!gender) return 'Chưa rõ';
  const upper = gender.trim().toUpperCase();
  if (upper === 'MALE' || upper === 'NAM') return 'Nam';
  if (upper === 'FEMALE' || upper === 'NỮ' || upper === 'NU') return 'Nữ';
  return gender;
}

function computeAgeFromDob(dateOfBirth: string | null): number {
  if (!dateOfBirth) return 0;
  const parsed = new Date(dateOfBirth);
  if (Number.isNaN(parsed.getTime())) return 0;
  return Math.max(0, new Date().getUTCFullYear() - parsed.getUTCFullYear());
}

function formatIssuedTime(issuedAt: string | null): string {
  if (!issuedAt) return 'Chưa rõ giờ';
  const parsed = new Date(issuedAt);
  if (Number.isNaN(parsed.getTime())) return issuedAt;
  return parsed.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
}

/**
 * Tra cứu danh sách đơn thuốc tại nhà thuốc (`GET /pharmacy/prescriptions`).
 */
export async function searchPharmacyPrescriptions(
  params?: PrescriptionSearchQueryParams,
): Promise<PrescriptionSearchResult> {
  try {
    const cleanParams: Record<string, string | number> = {};
    if (params?.query?.trim()) cleanParams.query = params.query.trim();
    if (params?.status && params.status !== 'ALL') cleanParams.status = params.status;
    if (params?.page) cleanParams.page = params.page;
    cleanParams.pageSize = params?.pageSize ?? 50;

    const { data } = await apiClient.get<PrescriptionSearchApiResponseDto>(
      '/pharmacy/prescriptions',
      { params: cleanParams },
    );

    const items: PrescriptionSearchItem[] = (data.items ?? []).map((dto, index) => {
      const normalizedStatus = normalizePrescriptionStatus(dto.status);
      return {
        id: String(dto.id),
        queueNo: String(index + 1).padStart(2, '0'),
        rxCode: dto.prescriptionCode,
        issuedTime: formatIssuedTime(dto.issuedAt),
        patientName: dto.patientName,
        patientCode: dto.patientCode,
        gender: mapGenderLabel(dto.gender),
        age: computeAgeFromDob(dto.dateOfBirth),
        doctorName: dto.doctorName || 'Chưa cập nhật',
        roomLabel: `Lượt khám ${dto.visitCode}`,
        facility: 'Nhà thuốc Ngoại trú',
        diagnosisText: `Lượt khám ${dto.visitCode}`,
        itemCount: dto.itemCount,
        totalUnits: dto.itemCount,
        totalPriceFormatted: 'Chưa tính giá lẻ',
        status: normalizedStatus,
      };
    });

    const metrics: PrescriptionSearchMetrics = {
      pendingApproval: items.filter((i) => i.status === 'PENDING_APPROVAL').length,
      preparing: items.filter((i) => i.status === 'PREPARING').length,
      readyForDispense: items.filter((i) => i.status === 'READY_FOR_DISPENSE').length,
      completedInShift: items.filter((i) => i.status === 'COMPLETED').length,
    };

    return {
      items,
      totalCount: data.totalCount ?? items.length,
      metrics,
    };
  } catch (error) {
    throw extractPharmacyApiError(error, 'Không thể tải danh sách đơn thuốc tại nhà thuốc.');
  }
}

interface PreparePrescriptionContextDto {
  prescriptionId: number;
  rxCode: string;
  visitId: number;
  visitCode: string;
  patientCode: string;
  patientName: string;
  gender: string | null;
  ageLabel: string | null;
  doctorName: string | null;
  roomLabel: string | null;
  primaryDiagnosis: string | null;
  secondaryDiagnosis: string | null;
  criticalAllergy: string | null;
  paymentBadge: string | null;
  status: string;
  dispenseId: number | null;
  issuedAt: string | null;
  items: Array<{
    id: number;
    orderIndex: number;
    medicineId: number;
    medicineCode: string | null;
    drugName: string;
    activeIngredient: string | null;
    strength: string | null;
    dosageForm: string | null;
    usageInstruction: string | null;
    prescribedQty: number;
    pickedQty: number;
    unit: string | null;
    selectedBatchId: number | null;
    selectedBatchCode: string | null;
    isPrepared: boolean;
    availableBatches: Array<{
      batchId: number;
      batchCode: string;
      expiryDate: string;
      availableStock: number;
      shelfLocation: string;
      isFefoRecommended?: boolean;
    }>;
  }>;
  gppChecks: Array<{
    id: string;
    title: string;
    description: string;
    checked: boolean;
  }>;
}

/**
 * Lấy ngữ cảnh soạn thuốc & danh sách lô FEFO (`GET /pharmacy/prescriptions/:idOrCode/prepare`).
 */
export async function fetchPreparePrescriptionContext(
  idOrCode: string,
): Promise<PreparePrescriptionContext> {
  const trimmed = idOrCode.trim();
  if (!trimmed) {
    throw new Error('Mã đơn thuốc không được để trống.');
  }

  try {
    const { data } = await apiClient.get<PreparePrescriptionContextDto>(
      `/pharmacy/prescriptions/${encodeURIComponent(trimmed)}/prepare`,
    );

    return {
      prescriptionId: data.prescriptionId,
      rxCode: data.rxCode,
      visitCode: data.visitCode,
      patientName: data.patientName,
      patientCode: data.patientCode,
      gender: data.gender || 'Chưa cập nhật',
      ageLabel: data.ageLabel || 'Chưa rõ tuổi',
      doctorName: data.doctorName || 'Chưa cập nhật',
      roomLabel: data.roomLabel || 'Phòng khám ngoại trú',
      primaryDiagnosis: data.primaryDiagnosis || 'Chưa ghi nhận chẩn đoán chính',
      secondaryDiagnosis: data.secondaryDiagnosis || '',
      criticalAllergy: data.criticalAllergy || '',
      paymentBadge: data.paymentBadge || data.status,
      status: data.status,
      items: (data.items ?? []).map((item) => {
        const batches = (item.availableBatches ?? []).map((b) => ({
          batchId: b.batchId,
          batchCode: b.batchCode,
          expiryDate: b.expiryDate,
          availableStock: Number(b.availableStock),
          shelfLocation: b.shelfLocation || 'Chưa cập nhật kệ',
          isFefoRecommended: Boolean(b.isFefoRecommended),
        }));
        const fallbackBatch = batches.find((b) => b.isFefoRecommended) ?? batches[0];

        return {
          id: String(item.id),
          prescriptionItemId: item.id,
          orderIndex: item.orderIndex,
          drugName: item.drugName,
          dosageForm: item.dosageForm || item.strength || 'Chưa cập nhật dạng bào chế',
          usageInstruction: item.usageInstruction || 'Dùng theo chỉ định của bác sĩ',
          prescribedQty: Number(item.prescribedQty),
          pickedQty: Number(item.pickedQty),
          unit: item.unit || 'Đơn vị',
          availableBatches: batches,
          selectedBatchId: item.selectedBatchId ?? fallbackBatch?.batchId ?? null,
          selectedBatchCode: item.selectedBatchCode || fallbackBatch?.batchCode || '',
          isPrepared: Boolean(item.isPrepared),
        };
      }),
      gppChecks: data.gppChecks ?? [],
    };
  } catch (error) {
    throw extractPharmacyApiError(error, 'Không thể tải thông tin soạn thuốc của đơn thuốc.');
  }
}

/**
 * Xác nhận soạn thuốc & phân bổ lô FEFO (`POST /pharmacy/prescriptions/:idOrCode/prepare`).
 */
export async function submitPreparePrescription(
  idOrCode: string,
  payload: PreparePrescriptionPayload,
): Promise<PreparePrescriptionResponse> {
  const trimmed = idOrCode.trim();
  if (!trimmed) {
    throw new Error('Mã đơn thuốc không được để trống.');
  }

  try {
    const { data } = await apiClient.post<PreparePrescriptionResponse>(
      `/pharmacy/prescriptions/${encodeURIComponent(trimmed)}/prepare`,
      payload,
    );
    return data;
  } catch (error) {
    throw extractPharmacyApiError(error, 'Không thể xác nhận hoàn tất soạn đơn thuốc.');
  }
}

interface DispenseConfirmContextDto {
  prescriptionId: number;
  rxCode: string;
  ticketNo: string;
  visitCode: string;
  issuedAt: string | null;
  doctorName: string | null;
  totalPaidFormatted: string;
  paymentMethodLabel: string;
  paymentStatus: string;
  regimenBadge: string;
  receiver: {
    fullName: string;
    patientCode: string;
    identityCard: string;
    phone: string;
    receiverName: string;
    relationship: string;
    isIdentityVerified: boolean;
  };
  medications: Array<{
    id: string;
    orderIndex: number;
    prescriptionItemId: number;
    medicineId: number;
    drugName: string;
    qtyLabel: string;
    prescribedQuantity: number;
    dispensedQuantity: number;
    unit: string;
    batchCode: string | null;
    batchId: number | null;
    expiryDate: string | null;
    dosageInstruction: string;
    warningNote: string;
    tagLabel?: string | null;
    tagTone?: 'sky' | 'rose' | 'amber' | null;
    shelfLocation?: string | null;
    unitPrice?: number | null;
    unitPriceFormatted?: string | null;
    lineTotal?: number | null;
    lineTotalFormatted?: string | null;
  }>;
  checks: Array<{
    id: string;
    label: string;
    checked: boolean;
  }>;
  canDispense: boolean;
  disabledReason?: string | null;
  dispenseStatus: string;
}

/**
 * Lấy ngữ cảnh bàn giao thuốc tại quầy (`GET /pharmacy/prescriptions/:idOrCode/dispense`).
 */
export async function fetchDispenseConfirmContext(
  idOrCode: string,
): Promise<DispenseConfirmContext> {
  const trimmed = idOrCode.trim();
  if (!trimmed) {
    throw new Error('Mã đơn thuốc không được để trống.');
  }

  try {
    const { data } = await apiClient.get<DispenseConfirmContextDto>(
      `/pharmacy/prescriptions/${encodeURIComponent(trimmed)}/dispense`,
    );

    return {
      prescriptionId: data.prescriptionId,
      rxCode: data.rxCode,
      ticketNo: data.ticketNo,
      visitCode: data.visitCode,
      issuedAt: data.issuedAt || 'Chưa cập nhật',
      doctorName: data.doctorName || 'Chưa cập nhật',
      totalPaidFormatted: data.totalPaidFormatted || 'Chưa cập nhật',
      paymentMethodLabel: data.paymentMethodLabel || data.paymentStatus,
      paymentStatus: data.paymentStatus,
      regimenBadge: data.regimenBadge,
      receiver: data.receiver,
      medications: (data.medications ?? []).map((med) => ({
        id: med.id,
        orderIndex: med.orderIndex,
        prescriptionItemId: med.prescriptionItemId,
        medicineId: med.medicineId,
        drugName: med.drugName,
        qtyLabel: med.qtyLabel,
        prescribedQuantity: med.prescribedQuantity,
        dispensedQuantity: med.dispensedQuantity,
        unit: med.unit,
        batchCode: med.batchCode || 'Chưa chọn lô',
        batchId: med.batchId,
        expiryDate: med.expiryDate || 'Chưa cập nhật',
        dosageInstruction: med.dosageInstruction,
        warningNote: med.warningNote,
        tagLabel: med.tagLabel ?? undefined,
        tagTone: med.tagTone ?? undefined,
        shelfLocation: med.shelfLocation,
        unitPrice: med.unitPrice,
        unitPriceFormatted: med.unitPriceFormatted,
        lineTotal: med.lineTotal,
        lineTotalFormatted: med.lineTotalFormatted,
      })),
      checks: data.checks ?? [],
      canDispense: data.canDispense,
      disabledReason: data.disabledReason,
      dispenseStatus: data.dispenseStatus,
    };
  } catch (error) {
    throw extractPharmacyApiError(error, 'Không thể tải ngữ cảnh bàn giao đơn thuốc.');
  }
}

/**
 * Xác nhận bàn giao thuốc & trừ tồn kho (`POST /pharmacy/prescriptions/:idOrCode/dispense`).
 */
export async function confirmMedicineDispense(
  idOrCode: string,
  payload: ConfirmDispensePayload,
): Promise<DispensingResultDetail> {
  const trimmed = idOrCode.trim();
  if (!trimmed) {
    throw new Error('Mã đơn thuốc không được để trống.');
  }

  try {
    const { data } = await apiClient.post<DispensingResultDetail>(
      `/pharmacy/prescriptions/${encodeURIComponent(trimmed)}/dispense`,
      payload,
    );
    return data;
  } catch (error) {
    throw extractPharmacyApiError(error, 'Không thể xác nhận bàn giao thuốc.');
  }
}

/**
 * Lấy chi tiết kết quả cấp phát thuốc / biên nhận bàn giao (`GET /pharmacy/dispenses/:idOrCode`).
 */
export async function fetchDispensingResult(idOrCode: string): Promise<DispensingResultDetail> {
  const trimmed = idOrCode?.trim() || '';
  if (!trimmed) {
    throw new Error('Mã phiếu cấp phát không được để trống.');
  }

  try {
    const { data } = await apiClient.get<DispensingResultDetail>(
      `/pharmacy/dispenses/${encodeURIComponent(trimmed)}`,
    );
    return data;
  } catch (error) {
    throw extractPharmacyApiError(error, 'Không tìm thấy biên nhận bàn giao thuốc.');
  }
}
