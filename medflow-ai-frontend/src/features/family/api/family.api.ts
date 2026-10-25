import { apiClient } from '@/shared/lib/axios';

import { mockFamilyMembers } from '../data/family-mock';
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

export async function fetchFamilyMemberById(memberId: string): Promise<FamilyMember> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.get<FamilyMember>(
        `/patients/me/family-members/${encodeURIComponent(memberId)}`,
      );
      return data;
    } catch (error) {
      const fallback = mockFamilyMembers.find((member) => member.id === memberId);
      if (fallback) {
        return fallback;
      }
      throw error;
    }
  }

  const found = mockFamilyMembers.find((member) => member.id === memberId);
  if (!found) {
    throw new Error('Không tìm thấy hồ sơ người thân hoặc bạn không có quyền truy cập.');
  }

  return found;
}

