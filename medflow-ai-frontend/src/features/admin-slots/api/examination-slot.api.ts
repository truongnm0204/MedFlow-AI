import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import type { ExaminationSlotFormValues } from '../schemas/examination-slot.schema';
import type {
  AffectedBookingsAction,
  CloseSlotPayload,
  CloseSlotReasonCode,
  ExaminationSlot,
  ServiceType,
  SlotStatus,
} from '../types/examination-slot.types';

export interface ExaminationSlotApiItemDto {
  id: number | string;
  code: string;
  doctorId?: number | string | null;
  doctorName?: string | null;
  serviceId?: number | string | null;
  serviceName?: string | null;
  serviceCode?: string | null;
  serviceType?: string | null;
  specialty?: string | null;
  roomId?: number | null;
  room?: string | null;
  capacityBooked?: number;
  capacityMax?: number;
  date: string;
  dateDisplay?: string | null;
  startTime: string;
  endTime: string;
  status?: string | null;
  facility?: string | null;
  closedAt?: string | null;
  closeReason?: string | null;
  closeReasonCode?: string | null;
  affectedBookingsAction?: string | null;
}

interface ExaminationSlotAdminListResponseDto {
  data?: ExaminationSlotApiItemDto[];
  items?: ExaminationSlotApiItemDto[];
  total?: number;
}

interface ProblemDetailsBody {
  detail?: string;
  title?: string;
  message?: string;
}

function extractSlotApiError(error: unknown, fallbackMessage: string): Error {
  if (axios.isAxiosError(error)) {
    const body = error.response?.data as ProblemDetailsBody | undefined;
    const msg = body?.detail || body?.message || body?.title;
    if (msg && msg.trim().length > 0) return new Error(msg.trim());
  }
  if (error instanceof Error) return error;
  return new Error(fallbackMessage);
}

function normalizeServiceType(raw?: string | null): ServiceType {
  const upper = (raw ?? '').toUpperCase();
  if (upper === 'DIAGNOSTIC') return 'DIAGNOSTIC';
  if (upper === 'INPATIENT') return 'INPATIENT';
  return 'OUTPATIENT';
}

function normalizeSlotStatus(raw?: string | null): SlotStatus {
  const upper = (raw ?? '').toUpperCase();
  if (upper === 'CLOSED') return 'CLOSED';
  if (upper === 'FULL') return 'FULL';
  return 'OPEN';
}

function parseNumericId(raw?: string | number | null): number | null {
  if (raw == null || raw === '') return null;
  const n = Number(raw);
  return Number.isFinite(n) ? n : null;
}

export function mapExaminationSlotDto(dto: ExaminationSlotApiItemDto): ExaminationSlot {
  const dateParts = dto.date ? dto.date.split('-') : [];
  const computedDisplay =
    dateParts.length === 3 ? `${dateParts[2]}/${dateParts[1]}` : (dto.date ?? '');

  return {
    id: String(dto.id),
    code: dto.code,
    doctorId: dto.doctorId != null ? String(dto.doctorId) : '',
    doctorName: dto.doctorName || 'Chưa phân công',
    serviceId: dto.serviceId != null ? String(dto.serviceId) : '',
    serviceName: dto.serviceName || 'Dịch vụ khám',
    serviceCode: dto.serviceCode || '',
    serviceType: normalizeServiceType(dto.serviceType),
    specialty: dto.specialty || 'Đa khoa',
    room: dto.room || 'Chưa xếp phòng',
    capacityBooked: dto.capacityBooked ?? 0,
    capacityMax: dto.capacityMax ?? 1,
    date: dto.date,
    dateDisplay: dto.dateDisplay || computedDisplay,
    startTime: dto.startTime,
    endTime: dto.endTime,
    status: normalizeSlotStatus(dto.status),
    facility: dto.facility ?? undefined,
    closedAt: dto.closedAt ?? undefined,
    closeReason: dto.closeReason ?? undefined,
    closeReasonCode: (dto.closeReasonCode as CloseSlotReasonCode | null | undefined) ?? undefined,
    affectedBookingsAction:
      (dto.affectedBookingsAction as AffectedBookingsAction | null | undefined) ?? undefined,
  };
}

export async function fetchAdminExaminationSlotsApi(): Promise<ExaminationSlot[]> {
  try {
    const { data } = await apiClient.get<
      ExaminationSlotAdminListResponseDto | ExaminationSlotApiItemDto[]
    >('/admin/examination-slots', {
      params: { page: 1, pageSize: 200 },
    });
    const raw = Array.isArray(data) ? data : (data.data ?? data.items ?? []);
    return raw.map(mapExaminationSlotDto);
  } catch (error) {
    throw extractSlotApiError(error, 'Không thể tải danh sách khung giờ khám.');
  }
}

export async function createAdminExaminationSlotApi(
  values: ExaminationSlotFormValues,
): Promise<ExaminationSlot> {
  try {
    const { data } = await apiClient.post<ExaminationSlotApiItemDto>('/admin/examination-slots', {
      serviceId: parseNumericId(values.serviceId),
      serviceCode: values.serviceCode,
      doctorId: parseNumericId(values.doctorId),
      doctorCode: values.doctorId || null,
      roomCode: values.room || null,
      date: values.date,
      startTime: values.startTime,
      endTime: values.endTime,
      capacity: values.capacityMax,
      status: values.status,
    });
    return mapExaminationSlotDto(data);
  } catch (error) {
    throw extractSlotApiError(error, 'Không thể tạo mới khung giờ khám.');
  }
}

export async function updateAdminExaminationSlotApi(
  slotId: string,
  values: ExaminationSlotFormValues,
): Promise<ExaminationSlot> {
  const numericId = slotId.replace(/\D/g, '') || slotId;
  try {
    const { data } = await apiClient.put<ExaminationSlotApiItemDto>(
      `/admin/examination-slots/${encodeURIComponent(numericId)}`,
      {
        serviceId: parseNumericId(values.serviceId),
        serviceCode: values.serviceCode,
        doctorId: parseNumericId(values.doctorId),
        doctorCode: values.doctorId || null,
        roomCode: values.room || null,
        date: values.date,
        startTime: values.startTime,
        endTime: values.endTime,
        capacity: values.capacityMax,
        status: values.status,
      },
    );
    return mapExaminationSlotDto(data);
  } catch (error) {
    throw extractSlotApiError(error, 'Không thể cập nhật thông tin khung giờ khám.');
  }
}

export async function closeAdminExaminationSlotApi(
  idOrCode: string,
  payload: CloseSlotPayload,
): Promise<{ success: boolean; message: string }> {
  try {
    const { data } = await apiClient.post<{ message?: string }>(
      `/admin/examination-slots/${encodeURIComponent(idOrCode)}/close`,
      {
        slotId: payload.slotId,
        reasonCode: payload.reasonCode,
        reasonText: payload.reasonText,
        reason: payload.reasonText,
        note: payload.note ?? null,
        notifyDoctor: payload.notifyDoctor,
        notifyPatients: payload.notifyPatients,
        affectedBookingsAction: payload.affectedBookingsAction,
        confirmUnderstood: true,
      },
    );
    return {
      success: true,
      message: data?.message || 'Khung giờ khám đã được đóng thành công.',
    };
  } catch (error) {
    throw extractSlotApiError(error, 'Không thể đóng khung giờ khám.');
  }
}

export async function reopenAdminExaminationSlotApi(
  idOrCode: string,
): Promise<{ success: boolean; message: string }> {
  try {
    const { data } = await apiClient.post<{ message?: string }>(
      `/admin/examination-slots/${encodeURIComponent(idOrCode)}/reopen`,
      {},
    );
    return {
      success: true,
      message: data?.message || 'Khung giờ khám đã được mở lại thành công.',
    };
  } catch (error) {
    throw extractSlotApiError(error, 'Không thể mở lại khung giờ khám.');
  }
}

export async function batchCloseAdminExaminationSlotsApi(
  ids: string[],
  payload: Omit<CloseSlotPayload, 'slotId'>,
): Promise<{ success: boolean; count: number }> {
  const numericIds = ids.map((id) => Number(id)).filter((num) => Number.isFinite(num) && num > 0);

  try {
    const { data } = await apiClient.post<{ closedCount?: number; count?: number }>(
      '/admin/examination-slots/batch-close',
      {
        ids,
        slotIds: numericIds.length > 0 ? numericIds : undefined,
        reasonCode: payload.reasonCode,
        reasonText: payload.reasonText,
        reason: payload.reasonText,
        note: payload.note ?? null,
        notifyDoctor: payload.notifyDoctor,
        notifyPatients: payload.notifyPatients,
        affectedBookingsAction: payload.affectedBookingsAction,
        confirmUnderstood: true,
      },
    );
    return {
      success: true,
      count: data?.closedCount ?? data?.count ?? ids.length,
    };
  } catch (error) {
    throw extractSlotApiError(error, 'Không thể đóng hàng loạt khung giờ khám.');
  }
}
