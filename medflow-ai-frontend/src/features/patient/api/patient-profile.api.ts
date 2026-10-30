import axios from 'axios';

import { useAuthStore } from '@/features/auth/store/auth.store';
import type { UserDto } from '@/features/auth/types/auth.types';
import { getRoleDisplayLabel, isStaffRole } from '@/features/auth/utils/role-landing';
import { apiClient } from '@/shared/lib/axios';

import { mockPatientProfile } from '../data/patient-profile.mock';
import type { GenderType, PatientProfile } from '../types/patient-profile.types';

export interface PatientProfileResponseDto {
  id: number;
  userId: number;
  patientCode: string;
  fullName: string;
  dateOfBirth: string | null;
  age: number | null;
  gender: string | null;
  phone: string | null;
  email: string | null;
  isEmailVerified: boolean;
  isIdentityVerified: boolean;
  maskedIdentification: string | null;
  status: string;
  address: string | null;
  bloodType: string | null;
  allowedActions: string[];
}

function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404 || error.response.status === 401;
}

function shouldUseHttpApi(): boolean {
  if ('mock' in apiClient.get) return true;
  if (import.meta.env.MODE === 'test') return false;
  return (
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'))
  );
}

function mapGender(gender: string | null, fallback: GenderType): GenderType {
  if (!gender) return fallback;
  const normalized = gender.trim().toUpperCase();
  if (normalized === 'MALE' || normalized === 'NAM') return 'Nam';
  if (normalized === 'FEMALE' || normalized === 'NỮ' || normalized === 'NU') return 'Nữ';
  if (normalized === 'OTHER' || normalized === 'KHÁC' || normalized === 'KHAC') return 'Khác';
  return fallback;
}

function formatIsoDateToDisplay(dateOfBirth: string | null, fallback: string): string {
  if (!dateOfBirth) return fallback;
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(dateOfBirth.trim());
  if (!match) return dateOfBirth;
  const [, year, month, day] = match;
  return `${day}/${month}/${year}`;
}

export function mapPatientProfileDtoToModel(
  dto: PatientProfileResponseDto,
  fallback: PatientProfile = mockPatientProfile,
): PatientProfile {
  return {
    ...fallback,
    id: dto.patientCode || String(dto.id),
    identity: {
      ...fallback.identity,
      fullName: (dto.fullName || fallback.identity.fullName).toUpperCase(),
      dateOfBirth: formatIsoDateToDisplay(dto.dateOfBirth, fallback.identity.dateOfBirth),
      age: dto.age ?? fallback.identity.age,
      gender: mapGender(dto.gender, fallback.identity.gender),
      identityNumber: dto.maskedIdentification ?? 'Chưa cập nhật CCCD',
      isIdentityVerified: dto.isIdentityVerified,
      pidNumber: dto.patientCode || fallback.identity.pidNumber,
    },
    vitals: {
      ...fallback.vitals,
      bloodType: dto.bloodType || fallback.vitals.bloodType,
    },
    contact: {
      ...fallback.contact,
      phone: dto.phone || fallback.contact.phone,
      email: dto.email || fallback.contact.email,
      isEmailVerified: dto.isEmailVerified,
      permanentAddress: dto.address || fallback.contact.permanentAddress,
    },
  };
}

export function mapStaffUserToProfileModel(
  user: UserDto,
  fallback: PatientProfile = mockPatientProfile,
): PatientProfile {
  const roleLabel = getRoleDisplayLabel(user.roles);
  const staffCode = user.staffCode || `USR-${user.id}`;

  return {
    ...fallback,
    id: staffCode,
    identity: {
      ...fallback.identity,
      fullName: (user.fullName || user.email || fallback.identity.fullName).toUpperCase(),
      identityNumber: `${staffCode} (${roleLabel})`,
      isIdentityVerified: true,
      pidNumber: staffCode,
    },
    contact: {
      ...fallback.contact,
      email: user.email || fallback.contact.email,
      isEmailVerified: true,
    },
  };
}

export async function getMyPatientProfileApi(): Promise<PatientProfile> {
  const authUser = useAuthStore.getState().user;
  const isStaffOnlyAccount =
    Boolean(authUser) && isStaffRole(authUser?.roles) && !authUser?.roles?.includes('PATIENT');

  if (isStaffOnlyAccount && authUser && !('mock' in apiClient.get)) {
    return mapStaffUserToProfileModel(authUser, mockPatientProfile);
  }

  if (shouldUseHttpApi()) {
    try {
      const { data } = await apiClient.get<PatientProfileResponseDto>('/patients/me/profile');
      return mapPatientProfileDtoToModel(data, mockPatientProfile);
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return mockPatientProfile;
}
