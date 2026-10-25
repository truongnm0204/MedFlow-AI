import { apiClient } from '@/shared/lib/axios';

import { mockFamilyMembers } from '../data/family-mock';
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

export async function createFamilyMember(payload: CreateFamilyMemberInput): Promise<FamilyMember> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.post<FamilyMember>('/patients/me/family-members', payload);
      return data;
    } catch (error) {
      // If network/endpoint not ready, fallback to mock generation
      console.warn('API error, falling back to mock creation:', error);
    }
  }

  // Calculate age from birthDate (DD/MM/YYYY or YYYY-MM-DD)
  let calculatedAge = 10;
  try {
    const parts = payload.birthDate.split(/[-/]/);
    const birthYear = parts.length === 3 ? parseInt(parts[0].length === 4 ? parts[0] : parts[2], 10) : 2015;
    calculatedAge = Math.max(1, new Date().getFullYear() - birthYear);
  } catch {
    calculatedAge = 10;
  }

  const badgeColor =
    payload.relationship === 'Con trai' || payload.relationship === 'Con gái'
      ? 'blue'
      : payload.relationship === 'Vợ' || payload.relationship === 'Chồng'
        ? 'rose'
        : 'amber';

  const avatar =
    payload.gender === 'Nam'
      ? 'https://images.unsplash.com/photo-1543332164-6e82f355badc?w=300&auto=format&fit=crop&q=80'
      : 'https://images.unsplash.com/photo-1544005313-94ddf0286df2?w=300&auto=format&fit=crop&q=80';

  const newMember: FamilyMember = {
    id: `FAM-${Date.now()}`,
    fullName: payload.fullName.trim().toUpperCase(),
    relationship: payload.relationship,
    relationshipBadgeColor: badgeColor,
    patientCode: `#PAT-2026-${Math.floor(1000 + Math.random() * 9000)}`,
    subStatus: 'Hồ sơ người thân mới',
    isPrimaryAccount: false,
    avatarUrl: avatar,
    birthDate: payload.birthDate,
    age: calculatedAge,
    gender: payload.gender,
    nationality: payload.nationality || 'Việt Nam',
    identificationNo: payload.identificationNo || undefined,
    phone: payload.phone || undefined,
    email: payload.email || undefined,
    address: payload.address,
    emergencyContactName: payload.emergencyContactName || 'Nguyễn Văn An',
    emergencyContactRelation: payload.emergencyContactRelation || 'Chủ tài khoản chính',
    emergencyPhone: payload.emergencyPhone || '0912 345 678',
    isIdentityVerified: true,
    permissions: {
      canManage: true,
      canBookAppointments: true,
      canViewMedicalRecords: true,
      relationshipStatus: 'ACTIVE',
      consentVerifiedAt: new Date().toLocaleDateString('vi-VN'),
    },
  };

  mockFamilyMembers.push(newMember);
  return newMember;
}

export async function updateFamilyMember(
  memberId: string,
  payload: UpdateFamilyMemberInput,
): Promise<FamilyMember> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.put<FamilyMember>(
        `/patients/me/family-members/${encodeURIComponent(memberId)}`,
        payload,
      );
      return data;
    } catch (error) {
      console.warn('API error, falling back to mock update:', error);
    }
  }

  const index = mockFamilyMembers.findIndex((m) => m.id === memberId);
  if (index === -1) {
    throw new Error('Không tìm thấy hồ sơ người thân cần cập nhật.');
  }

  const current = mockFamilyMembers[index];
  const updated: FamilyMember = {
    ...current,
    phone: payload.phone !== undefined ? payload.phone : current.phone,
    email: payload.email !== undefined ? payload.email : current.email,
    address: payload.address !== undefined ? payload.address : current.address,
    emergencyContactName:
      payload.emergencyContactName !== undefined
        ? payload.emergencyContactName
        : current.emergencyContactName,
    emergencyContactRelation:
      payload.emergencyContactRelation !== undefined
        ? payload.emergencyContactRelation
        : current.emergencyContactRelation,
    emergencyPhone:
      payload.emergencyPhone !== undefined ? payload.emergencyPhone : current.emergencyPhone,
  };

  mockFamilyMembers[index] = updated;
  return updated;
}

