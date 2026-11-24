import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import type { StaffCreateFormValues } from '../schemas/staff-create.schema';
import type { DeactivateStaffPayload } from '../types/deactivate-staff.types';
import type {
  StaffAccountItem,
  StaffAccountMetrics,
  StaffAccountStatus,
  StaffRole as StaffListRoleLabel,
} from '../types/staff-account.types';
import type {
  StaffAccountRecord,
  StaffAuthMethod,
  StaffRole as StaffRoleCode,
  UpdateStaffAccountInput,
} from '../types/staff-update.types';

interface StaffAccountApiItemDto {
  staffId: number;
  userId?: number;
  id?: string;
  code: string;
  fullName: string;
  email: string;
  roleCode?: string;
  role?: string;
  departmentId?: number | null;
  department?: string | null;
  hospitalId?: number | null;
  facility?: string | null;
  status?: string | null;
  authProvider?: string | null;
  authMethod?: string | null;
  pendingAssignments?: number;
}

interface StaffAccountListApiResponseDto {
  data?: StaffAccountApiItemDto[];
  items?: StaffAccountApiItemDto[];
  total?: number;
  metrics?: {
    total: number;
    active: number;
    locked: number;
    disabled: number;
  };
}

interface ProblemDetailsBody {
  detail?: string;
  title?: string;
  message?: string;
}

export interface StaffAccountListItemWithMeta extends StaffAccountItem {
  staffId: number;
  pendingAssignments: number;
}

export interface AdminStaffAccountsResult {
  items: StaffAccountListItemWithMeta[];
  records: StaffAccountRecord[];
  metrics: StaffAccountMetrics;
}

function extractStaffApiError(error: unknown, fallbackMessage: string): Error {
  if (axios.isAxiosError(error)) {
    const body = error.response?.data as ProblemDetailsBody | undefined;
    const msg = body?.detail || body?.message || body?.title;
    if (msg && msg.trim().length > 0) return new Error(msg.trim());
  }
  if (error instanceof Error) return error;
  return new Error(fallbackMessage);
}

function mapToRoleCode(roleCode?: string, roleLabel?: string): StaffRoleCode {
  const upper = (roleCode ?? '').toUpperCase();
  if (upper === 'DOCTOR') return 'DOCTOR';
  if (upper === 'TECHNICIAN' || upper === 'DIAGNOSTIC_TECH' || upper === 'PHARMACIST') {
    return 'TECHNICIAN';
  }
  if (upper === 'NURSE') return 'NURSE';
  if (upper === 'RECEPTIONIST') return 'RECEPTIONIST';

  const label = (roleLabel ?? '').toLowerCase();
  if (label.includes('bác sĩ')) return 'DOCTOR';
  if (label.includes('kỹ thuật') || label.includes('dược')) return 'TECHNICIAN';
  if (label.includes('điều dưỡng')) return 'NURSE';
  return 'RECEPTIONIST';
}

function mapToRoleLabel(roleCode: StaffRoleCode, rawLabel?: string): StaffListRoleLabel {
  if (
    rawLabel === 'Bác sĩ' ||
    rawLabel === 'Kỹ thuật viên' ||
    rawLabel === 'Điều dưỡng' ||
    rawLabel === 'Nhân viên quầy'
  ) {
    return rawLabel;
  }
  switch (roleCode) {
    case 'DOCTOR':
      return 'Bác sĩ';
    case 'TECHNICIAN':
      return 'Kỹ thuật viên';
    case 'NURSE':
      return 'Điều dưỡng';
    case 'RECEPTIONIST':
      return 'Nhân viên quầy';
  }
}

function normalizeStatus(status?: string | null): StaffAccountStatus {
  const upper = (status ?? '').toUpperCase();
  if (upper === 'LOCKED') return 'LOCKED';
  if (upper === 'DISABLED' || upper === 'INACTIVE') return 'DISABLED';
  return 'ACTIVE';
}

function normalizeAuthMethod(raw?: string | null): StaffAuthMethod {
  return (raw ?? '').toUpperCase() === 'GOOGLE' ? 'GOOGLE' : 'LOCAL';
}

export async function fetchAdminStaffAccountsApi(): Promise<AdminStaffAccountsResult> {
  try {
    const { data } = await apiClient.get<StaffAccountListApiResponseDto | StaffAccountApiItemDto[]>(
      '/admin/staff',
      { params: { page: 1, pageSize: 200 } },
    );

    const rawItems = Array.isArray(data) ? data : (data.data ?? data.items ?? []);

    const items: StaffAccountListItemWithMeta[] = rawItems.map((dto) => {
      const roleCode = mapToRoleCode(dto.roleCode, dto.role);
      const status = normalizeStatus(dto.status);
      return {
        id: dto.id || String(dto.staffId),
        staffId: dto.staffId,
        code: dto.code,
        fullName: dto.fullName,
        email: dto.email,
        role: mapToRoleLabel(roleCode, dto.role),
        department: dto.department || 'Chưa phân khoa',
        facility: dto.facility || 'Cơ sở y tế',
        status,
        pendingAssignments: dto.pendingAssignments ?? 0,
      };
    });

    const records: StaffAccountRecord[] = rawItems.map((dto) => ({
      staffCode: dto.code,
      fullName: dto.fullName,
      authMethod: normalizeAuthMethod(dto.authMethod ?? dto.authProvider),
      facility: dto.facility || '',
      email: dto.email,
      status: normalizeStatus(dto.status),
      role: mapToRoleCode(dto.roleCode, dto.role),
      department: dto.department || '',
    }));

    const apiMetrics = !Array.isArray(data) ? data.metrics : undefined;
    const metrics: StaffAccountMetrics = {
      total: apiMetrics?.total ?? items.length,
      active: apiMetrics?.active ?? items.filter((i) => i.status === 'ACTIVE').length,
      locked: apiMetrics?.locked ?? items.filter((i) => i.status === 'LOCKED').length,
      disabled: apiMetrics?.disabled ?? items.filter((i) => i.status === 'DISABLED').length,
    };

    return { items, records, metrics };
  } catch (error) {
    throw extractStaffApiError(error, 'Không thể tải danh sách tài khoản nhân sự.');
  }
}

export async function createAdminStaffAccountApi(values: StaffCreateFormValues): Promise<void> {
  try {
    await apiClient.post('/admin/staff-accounts', values);
  } catch (error) {
    throw extractStaffApiError(error, 'Không thể tạo tài khoản nhân sự.');
  }
}

export async function updateAdminStaffAccountApi(
  staffIdentifier: string,
  values: UpdateStaffAccountInput,
): Promise<void> {
  try {
    await apiClient.put(`/admin/staff-accounts/${encodeURIComponent(staffIdentifier)}`, values);
  } catch (error) {
    throw extractStaffApiError(error, 'Không thể cập nhật tài khoản nhân sự.');
  }
}

export async function deactivateAdminStaffAccountApi(
  payload: DeactivateStaffPayload,
): Promise<void> {
  const numericId = String(payload.staffId).replace(/\D/g, '') || String(payload.staffId);
  try {
    await apiClient.patch(`/admin/staff/${encodeURIComponent(numericId)}/deactivate`, {
      reason: payload.reason,
      note: payload.note ?? null,
    });
  } catch (error) {
    throw extractStaffApiError(error, 'Không thể vô hiệu hóa tài khoản nhân sự.');
  }
}
