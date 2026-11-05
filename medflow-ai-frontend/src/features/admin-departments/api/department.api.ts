import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { useDepartmentStore } from '../store/department.store';
import type {
  DeactivateDepartmentPayload,
  DepartmentDependencySummary,
} from '../types/department-deactivate.types';
import type { DepartmentItem } from '../types/department.types';

function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  return !error.response || error.response.status === 404 || error.response.status === 501;
}

function shouldUseHttpApi(): boolean {
  if ('mock' in apiClient.get || 'mock' in apiClient.put) return true;
  if (import.meta.env.MODE === 'test') return false;
  return (
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'))
  );
}

export async function fetchDepartmentsApi(): Promise<DepartmentItem[]> {
  if (shouldUseHttpApi()) {
    try {
      const { data } = await apiClient.get<DepartmentItem[]>('/admin/departments');
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return useDepartmentStore.getState().departments;
}

export async function checkDepartmentDependenciesApi(
  departmentId: string,
): Promise<DepartmentDependencySummary> {
  const store = useDepartmentStore.getState();
  const department = store.departments.find(
    (d) => d.id === departmentId || d.code === departmentId,
  );

  if (shouldUseHttpApi()) {
    try {
      const numericId = departmentId.replace(/\D/g, '');
      const { data } = await apiClient.get<DepartmentDependencySummary>(
        `/admin/departments/${numericId || departmentId}/dependencies`,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  const activeRooms = department?.activeRoomsCount ?? 0;
  const activeDoctors = department?.activeDoctorsCount ?? 0;
  const activeServices = department?.activeServicesCount ?? 0;
  const pendingAppointments = department?.pendingAppointmentsCount ?? 0;
  const hasDependencies =
    department?.hasActiveDependencies ??
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
  departmentId: string,
  payload?: DeactivateDepartmentPayload,
): Promise<{ success: boolean; message: string }> {
  const store = useDepartmentStore.getState();
  const department = store.departments.find(
    (d) => d.id === departmentId || d.code === departmentId,
  );

  // Check dependencies before deactivating
  const dependencyInfo = await checkDepartmentDependenciesApi(departmentId);
  if (!dependencyInfo.canDeactivate) {
    throw new Error(
      dependencyInfo.blockingReason ||
        'Không thể ngừng hoạt động khoa phòng khi còn phụ thuộc đang hoạt động.',
    );
  }

  if (shouldUseHttpApi()) {
    try {
      const numericId = departmentId.replace(/\D/g, '') || departmentId;
      await apiClient.put(`/admin/departments/${numericId}`, {
        code: department?.code,
        name: department?.name,
        isActive: false,
        reason: payload?.reason,
        note: payload?.note,
      });
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  store.deactivateDepartment(departmentId, payload);
  return {
    success: true,
    message: `Đã ngừng hoạt động khoa phòng ${department?.name ?? departmentId}.`,
  };
}

export async function reactivateDepartmentApi(
  departmentId: string,
): Promise<{ success: boolean; message: string }> {
  const store = useDepartmentStore.getState();
  const department = store.departments.find(
    (d) => d.id === departmentId || d.code === departmentId,
  );

  if (shouldUseHttpApi()) {
    try {
      const numericId = departmentId.replace(/\D/g, '') || departmentId;
      await apiClient.put(`/admin/departments/${numericId}`, {
        code: department?.code,
        name: department?.name,
        isActive: true,
      });
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  store.activateDepartment(departmentId);
  return {
    success: true,
    message: `Đã kích hoạt lại hoạt động cho khoa phòng ${department?.name ?? departmentId}.`,
  };
}
