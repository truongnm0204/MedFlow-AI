import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import type {
  DeactivateDepartmentPayload,
  DepartmentDependencySummary,
} from '../types/department-deactivate.types';
import type { DepartmentItem } from '../types/department.types';

interface DepartmentApiItemDto {
  id: number;
  code: string;
  name: string;
  hospitalId: number | null;
  facility: string;
  isActive: boolean;
  status: string;
  activeRoomsCount?: number;
  activeDoctorsCount?: number;
  activeServicesCount?: number;
  pendingAppointmentsCount?: number;
  hasActiveDependencies?: boolean;
}

interface DepartmentListApiResponseDto {
  data: DepartmentApiItemDto[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface HospitalFacilityOption {
  id: number;
  code: string;
  name: string;
}

interface ProblemDetailsBody {
  detail?: string;
  title?: string;
  message?: string;
}

function extractDepartmentApiError(error: unknown, fallbackMessage: string): Error {
  if (axios.isAxiosError(error)) {
    const body = error.response?.data as ProblemDetailsBody | undefined;
    const msg = body?.detail || body?.message || body?.title;
    if (msg && msg.trim().length > 0) return new Error(msg.trim());
  }
  if (error instanceof Error) return error;
  return new Error(fallbackMessage);
}

function mapDepartmentDto(dto: DepartmentApiItemDto): DepartmentItem {
  return {
    id: String(dto.id),
    code: dto.code,
    name: dto.name,
    facility: dto.facility || 'Cơ sở y tế',
    status: dto.isActive || dto.status === 'ACTIVE' ? 'ACTIVE' : 'INACTIVE',
    activeRoomsCount: dto.activeRoomsCount ?? 0,
    activeDoctorsCount: dto.activeDoctorsCount ?? 0,
    activeServicesCount: dto.activeServicesCount ?? 0,
    pendingAppointmentsCount: dto.pendingAppointmentsCount ?? 0,
    hasActiveDependencies: dto.hasActiveDependencies ?? false,
  };
}

export async function fetchDepartmentsApi(): Promise<DepartmentItem[]> {
  try {
    const { data } = await apiClient.get<DepartmentListApiResponseDto | DepartmentApiItemDto[]>(
      '/admin/departments',
      { params: { page: 1, pageSize: 100 } },
    );
    const raw = Array.isArray(data) ? data : (data.data ?? []);
    return raw.map(mapDepartmentDto);
  } catch (error) {
    throw extractDepartmentApiError(error, 'Không thể tải danh mục khoa phòng.');
  }
}

export async function fetchHospitalsForAdminApi(): Promise<HospitalFacilityOption[]> {
  try {
    const { data } =
      await apiClient.get<
        Array<{ id: number; hospitalCode?: string; code?: string; name: string }>
      >('/hospitals');
    const items = Array.isArray(data) ? data : [];
    return items.map((h) => ({
      id: h.id,
      code: h.hospitalCode || h.code || `HOS-${h.id}`,
      name: h.name,
    }));
  } catch (error) {
    throw extractDepartmentApiError(error, 'Không thể tải danh sách cơ sở y tế.');
  }
}

export async function createDepartmentApi(payload: {
  code: string;
  name: string;
  hospitalId?: number | null;
  isActive: boolean;
}): Promise<DepartmentItem> {
  try {
    const { data } = await apiClient.post<DepartmentApiItemDto>('/admin/departments', payload);
    return mapDepartmentDto(data);
  } catch (error) {
    throw extractDepartmentApiError(error, 'Không thể khai báo khoa phòng mới.');
  }
}

export async function updateDepartmentApi(
  id: string | number,
  payload: {
    code: string;
    name: string;
    hospitalId?: number | null;
    isActive: boolean;
  },
): Promise<DepartmentItem> {
  const numericId = String(id).replace(/\D/g, '') || String(id);
  try {
    const { data } = await apiClient.put<DepartmentApiItemDto>(
      `/admin/departments/${encodeURIComponent(numericId)}`,
      payload,
    );
    return mapDepartmentDto(data);
  } catch (error) {
    throw extractDepartmentApiError(error, 'Không thể cập nhật thông tin khoa phòng.');
  }
}

export async function checkDepartmentDependenciesApi(
  department: DepartmentItem,
): Promise<DepartmentDependencySummary> {
  const activeRooms = department.activeRoomsCount ?? 0;
  const activeDoctors = department.activeDoctorsCount ?? 0;
  const activeServices = department.activeServicesCount ?? 0;
  const pendingAppointments = department.pendingAppointmentsCount ?? 0;
  const hasDependencies =
    department.hasActiveDependencies ??
    (activeRooms > 0 || activeDoctors > 0 || pendingAppointments > 0);

  const canDeactivate = !hasDependencies;
  let blockingReason: string | undefined;

  if (!canDeactivate) {
    const reasons: string[] = [];
    if (activeRooms > 0) reasons.push(`${activeRooms} buồng khám`);
    if (activeDoctors > 0) reasons.push(`${activeDoctors} bác sĩ`);
    if (pendingAppointments > 0) reasons.push(`${pendingAppointments} lượt khám`);
    blockingReason = `Khoa đang có ${reasons.join(', ')} đang hoạt động.`;
  }

  return {
    activeRoomsCount: activeRooms,
    activeDoctorsCount: activeDoctors,
    activeServicesCount: activeServices,
    pendingAppointmentsCount: pendingAppointments,
    canDeactivate,
    blockingReason,
  };
}

export async function deactivateDepartmentApi(
  department: DepartmentItem,
  _payload?: DeactivateDepartmentPayload,
): Promise<{ success: boolean; message: string }> {
  const dependencyInfo = await checkDepartmentDependenciesApi(department);
  if (!dependencyInfo.canDeactivate) {
    throw new Error(
      dependencyInfo.blockingReason ||
        'Không thể ngừng hoạt động khoa phòng khi còn phụ thuộc đang hoạt động.',
    );
  }

  await updateDepartmentApi(department.id, {
    code: department.code,
    name: department.name,
    isActive: false,
  });

  return {
    success: true,
    message: `Đã ngừng hoạt động khoa phòng ${department.name}.`,
  };
}

export async function reactivateDepartmentApi(
  department: DepartmentItem,
): Promise<{ success: boolean; message: string }> {
  await updateDepartmentApi(department.id, {
    code: department.code,
    name: department.name,
    isActive: true,
  });

  return {
    success: true,
    message: `Đã kích hoạt lại hoạt động cho khoa phòng ${department.name}.`,
  };
}
