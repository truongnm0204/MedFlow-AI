import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import type { RoomCapacity, RoomRecord, RoomType } from '../types/room-update.types';
import type { RoomItem, RoomMetrics, RoomStatus } from '../types/room.types';

export interface ExaminationRoomApiItemDto {
  id: number;
  roomCode: string;
  name: string;
  hospitalId?: number | null;
  facility?: string | null;
  departmentId?: number | null;
  departmentCode?: string | null;
  departmentName?: string | null;
  roomType?: string | null;
  location?: string | null;
  capacity?: number | null;
  status?: string | null;
  isAvailable?: boolean;
  isActive?: boolean;
  hasActiveDependencies?: boolean;
  assignedDoctorsCount?: number;
  activeVisitsCount?: number;
  activeSlotsCount?: number;
}

interface ExaminationRoomMetricsApiDto {
  total: number;
  active: number;
  inactive: number;
  departments: number;
}

interface ExaminationRoomListApiResponseDto {
  data?: ExaminationRoomApiItemDto[];
  total?: number;
  page?: number;
  pageSize?: number;
  totalPages?: number;
  metrics?: ExaminationRoomMetricsApiDto;
}

interface ProblemDetailsBody {
  detail?: string;
  title?: string;
  message?: string;
}

export interface AdminRoomsCatalogResult {
  rooms: RoomItem[];
  records: RoomRecord[];
  metrics: RoomMetrics;
}

function extractRoomApiError(error: unknown, fallbackMessage: string): Error {
  if (axios.isAxiosError(error)) {
    const body = error.response?.data as ProblemDetailsBody | undefined;
    const msg = body?.detail || body?.message || body?.title;
    if (msg && msg.trim().length > 0) return new Error(msg.trim());
  }
  if (error instanceof Error) return error;
  return new Error(fallbackMessage);
}

function normalizeRoomStatus(dto: ExaminationRoomApiItemDto): RoomStatus {
  const upper = (dto.status ?? '').toUpperCase();
  if (upper === 'MAINTENANCE') return 'MAINTENANCE';
  if (upper === 'INACTIVE' || dto.isActive === false) return 'INACTIVE';
  return 'ACTIVE';
}

function normalizeRoomType(raw?: string | null): RoomType {
  return (raw ?? '').toUpperCase() === 'PROCEDURAL' ? 'PROCEDURAL' : 'OUTPATIENT';
}

function normalizeRoomCapacity(raw?: number | null): RoomCapacity {
  const value = raw ?? 1;
  if (value <= 1) return 1;
  if (value === 2) return 2;
  if (value === 3) return 3;
  return 4;
}

function mapRoomItem(dto: ExaminationRoomApiItemDto): RoomItem {
  const doctors = dto.assignedDoctorsCount ?? 0;
  const visits = dto.activeVisitsCount ?? 0;
  const slots = dto.activeSlotsCount ?? 0;
  const hasDeps = dto.hasActiveDependencies ?? (doctors > 0 || visits > 0 || slots > 0);

  return {
    id: String(dto.id),
    roomCode: dto.roomCode,
    roomName: dto.name,
    department: dto.departmentName || 'Chưa phân khoa',
    location: dto.location || '—',
    facility: dto.facility || 'Cơ sở y tế',
    status: normalizeRoomStatus(dto),
    capacityPerSlot: dto.capacity ?? 1,
    hasActiveDependencies: hasDeps,
    assignedDoctorsCount: doctors,
    activeVisitsCount: visits,
    activeSlotsCount: slots,
  };
}

function mapRoomRecord(dto: ExaminationRoomApiItemDto): RoomRecord {
  return {
    id: String(dto.id),
    code: dto.roomCode,
    name: dto.name,
    type: normalizeRoomType(dto.roomType),
    department: dto.departmentName || '',
    location: dto.location || '',
    capacity: normalizeRoomCapacity(dto.capacity),
    status: normalizeRoomStatus(dto),
  };
}

export async function fetchAdminRoomsApi(): Promise<AdminRoomsCatalogResult> {
  try {
    const { data } = await apiClient.get<
      ExaminationRoomListApiResponseDto | ExaminationRoomApiItemDto[]
    >('/admin/rooms', {
      params: { page: 1, pageSize: 200 },
    });

    const rawItems = Array.isArray(data) ? data : (data.data ?? []);
    const rooms = rawItems.map(mapRoomItem);
    const records = rawItems.map(mapRoomRecord);

    const apiMetrics = !Array.isArray(data) ? data.metrics : undefined;
    const activeCount = rooms.filter((r) => r.status === 'ACTIVE').length;
    const maintenanceCount = rooms.filter((r) => r.status !== 'ACTIVE').length;
    const deptCount = new Set(
      rooms.map((r) => r.department).filter((d) => d && d !== 'Chưa phân khoa'),
    ).size;

    const metrics: RoomMetrics = {
      totalRooms: apiMetrics?.total ?? rooms.length,
      activeRooms: apiMetrics?.active ?? activeCount,
      maintenanceRooms: apiMetrics?.inactive ?? maintenanceCount,
      linkedDepartments: apiMetrics?.departments ?? deptCount,
    };

    return { rooms, records, metrics };
  } catch (error) {
    throw extractRoomApiError(error, 'Không thể tải danh mục buồng khám.');
  }
}

export async function createAdminRoomApi(payload: {
  code: string;
  name: string;
  type: RoomType;
  department: string;
  departmentId?: number | null;
  location: string;
  capacity: number;
  status: RoomStatus;
}): Promise<RoomItem> {
  try {
    const { data } = await apiClient.post<ExaminationRoomApiItemDto>('/admin/rooms', {
      roomCode: payload.code,
      code: payload.code,
      name: payload.name,
      departmentId: payload.departmentId ?? null,
      roomType: payload.type,
      location: payload.location,
      capacity: payload.capacity,
      status: payload.status,
    });
    return mapRoomItem(data);
  } catch (error) {
    throw extractRoomApiError(error, 'Không thể khai báo buồng khám mới.');
  }
}

export async function updateAdminRoomApi(
  id: string | number,
  payload: {
    roomCode?: string;
    name: string;
    type?: RoomType;
    department: string;
    departmentId?: number | null;
    location: string;
    capacity: number;
    status: RoomStatus;
  },
): Promise<RoomItem> {
  const numericId = String(id).replace(/\D/g, '') || String(id);
  try {
    const { data } = await apiClient.put<ExaminationRoomApiItemDto>(
      `/admin/rooms/${encodeURIComponent(numericId)}`,
      {
        roomCode: payload.roomCode,
        code: payload.roomCode,
        name: payload.name,
        departmentId: payload.departmentId ?? null,
        department: payload.department,
        roomType: payload.type,
        type: payload.type,
        location: payload.location,
        capacity: payload.capacity,
        status: payload.status,
        isActive: payload.status !== 'INACTIVE',
        isAvailable: payload.status === 'ACTIVE',
      },
    );
    return mapRoomItem(data);
  } catch (error) {
    throw extractRoomApiError(error, 'Không thể cập nhật thông tin buồng khám.');
  }
}
