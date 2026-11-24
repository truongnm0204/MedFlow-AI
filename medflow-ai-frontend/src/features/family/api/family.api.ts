import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import type {
  CreateFamilyMemberInput,
  UpdateFamilyMemberInput,
} from '../schemas/family-member.schema';
import type { FamilyMember } from '../types/family.types';

export interface UpdateFamilyMemberContactPayload {
  phone?: string;
  email?: string;
  address?: string;
  guardian?: string;
  emergencyContactName?: string;
  emergencyContactRelation?: string;
  emergencyPhone?: string;
}

interface ProblemDetailsBody {
  detail?: string;
  title?: string;
  message?: string;
}

function extractApiError(error: unknown, fallbackMessage: string): Error {
  if (axios.isAxiosError(error)) {
    const body = error.response?.data as ProblemDetailsBody | undefined;
    const message = body?.detail || body?.message || body?.title;
    if (message && message.trim().length > 0) {
      return new Error(message.trim());
    }
  }
  if (error instanceof Error) {
    return error;
  }
  return new Error(fallbackMessage);
}

export async function fetchFamilyMembers(): Promise<FamilyMember[]> {
  try {
    const { data } = await apiClient.get<FamilyMember[]>('/patients/me/family-members');
    return Array.isArray(data) ? data : [];
  } catch (error) {
    throw extractApiError(error, 'Không thể tải danh sách hồ sơ người thân.');
  }
}

export async function fetchFamilyMemberById(memberId: string): Promise<FamilyMember> {
  try {
    const { data } = await apiClient.get<FamilyMember>(
      `/patients/me/family-members/${encodeURIComponent(memberId.trim())}`,
    );
    return data;
  } catch (error) {
    throw extractApiError(
      error,
      'Không tìm thấy hồ sơ người thân hoặc bạn không có quyền truy cập.',
    );
  }
}

export async function createFamilyMember(payload: CreateFamilyMemberInput): Promise<FamilyMember> {
  try {
    const { data } = await apiClient.post<FamilyMember>('/patients/me/family-members', payload);
    return data;
  } catch (error) {
    throw extractApiError(error, 'Không thể tạo hồ sơ người thân. Vui lòng kiểm tra lại dữ liệu.');
  }
}

export async function updateFamilyMember(
  memberId: string,
  payload: UpdateFamilyMemberInput,
): Promise<FamilyMember> {
  try {
    const { data } = await apiClient.put<FamilyMember>(
      `/patients/me/family-members/${encodeURIComponent(memberId.trim())}`,
      payload,
    );
    return data;
  } catch (error) {
    throw extractApiError(error, 'Không thể cập nhật thông tin hồ sơ người thân.');
  }
}

export async function removeFamilyMember(memberId: string, reason?: string): Promise<void> {
  try {
    await apiClient.delete(`/patients/me/family-members/${encodeURIComponent(memberId.trim())}`, {
      data: { reason },
    });
  } catch (error) {
    throw extractApiError(error, 'Không thể gỡ liên kết hồ sơ người thân.');
  }
}
