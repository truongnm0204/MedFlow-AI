import { useAuthStore } from '@/features/auth/store/auth.store';
import type { UserDto } from '@/features/auth/types/auth.types';
import { getRoleDisplayLabel, isStaffRole } from '@/features/auth/utils/role-landing';
import { apiClient } from '@/shared/lib/axios';

import type {
  GenderCode,
  GenderType,
  PatientProfile,
  UpdateMyPatientProfileInput,
} from '../types/patient-profile.types';

const UNSET_LABEL = 'Chưa cập nhật';

export interface PatientEmergencyContactDto {
  fullName?: string | null;
  relationship?: string | null;
  phone?: string | null;
}

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
  emergencyContact?: PatientEmergencyContactDto | null;
}

function mapGender(gender: string | null): GenderType {
  if (!gender) return UNSET_LABEL;
  const normalized = gender.trim().toUpperCase();
  if (normalized === 'MALE' || normalized === 'NAM') return 'Nam';
  if (normalized === 'FEMALE' || normalized === 'NỮ' || normalized === 'NU') return 'Nữ';
  if (normalized === 'OTHER' || normalized === 'KHÁC' || normalized === 'KHAC') return 'Khác';
  return UNSET_LABEL;
}

function mapGenderCode(gender: string | null): GenderCode | null {
  if (!gender) return null;
  const normalized = gender.trim().toUpperCase();
  if (normalized === 'MALE' || normalized === 'NAM') return 'MALE';
  if (normalized === 'FEMALE' || normalized === 'NỮ' || normalized === 'NU') return 'FEMALE';
  if (normalized === 'OTHER' || normalized === 'KHÁC' || normalized === 'KHAC') return 'OTHER';
  return null;
}

function formatIsoDateToDisplay(dateOfBirth: string | null): string {
  if (!dateOfBirth || !dateOfBirth.trim()) return UNSET_LABEL;
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(dateOfBirth.trim());
  if (!match) return dateOfBirth.trim();
  const [, year, month, day] = match;
  return `${day}/${month}/${year}`;
}

export function mapPatientProfileDtoToModel(dto: PatientProfileResponseDto): PatientProfile {
  const pid = dto.patientCode?.trim() || String(dto.id);
  const fullName = (dto.fullName?.trim() || UNSET_LABEL).toUpperCase();
  const ec = dto.emergencyContact;

  return {
    id: pid,
    avatarUrl: null,
    identity: {
      fullName,
      dateOfBirth: formatIsoDateToDisplay(dto.dateOfBirth),
      rawDateOfBirthIso: dto.dateOfBirth?.trim() || null,
      age: dto.age ?? null,
      gender: mapGender(dto.gender),
      rawGenderCode: mapGenderCode(dto.gender),
      nationality: UNSET_LABEL,
      identityNumber: dto.maskedIdentification?.trim() || 'Chưa cập nhật CCCD',
      identityIssueDate: UNSET_LABEL,
      identityIssuePlace: UNSET_LABEL,
      isIdentityVerified: Boolean(dto.isIdentityVerified),
      pidNumber: pid,
    },
    vitals: {
      bloodType: dto.bloodType?.trim() || UNSET_LABEL,
      heightCm: null,
      weightKg: null,
    },
    shortcuts: {
      familyMembersCount: null,
      outpatientVisitsCount: null,
      activePrescriptionsCount: null,
    },
    contact: {
      phone: dto.phone?.trim() || UNSET_LABEL,
      isPhoneVerified: false,
      email: dto.email?.trim() || UNSET_LABEL,
      isEmailVerified: Boolean(dto.isEmailVerified),
      permanentAddress: dto.address?.trim() || UNSET_LABEL,
      emergencyContact: {
        fullName: ec?.fullName?.trim() || UNSET_LABEL,
        relationship: ec?.relationship?.trim() || UNSET_LABEL,
        phone: ec?.phone?.trim() || UNSET_LABEL,
      },
    },
  };
}

export function mapStaffUserToProfileModel(user: UserDto): PatientProfile {
  const roleLabel = getRoleDisplayLabel(user.roles);
  const staffCode = user.staffCode || `USR-${user.id}`;
  const fullName = (user.fullName?.trim() || user.email?.trim() || staffCode).toUpperCase();

  return {
    id: staffCode,
    avatarUrl: null,
    identity: {
      fullName,
      dateOfBirth: UNSET_LABEL,
      rawDateOfBirthIso: null,
      age: null,
      gender: UNSET_LABEL,
      rawGenderCode: null,
      nationality: UNSET_LABEL,
      identityNumber: `${staffCode} (${roleLabel})`,
      identityIssueDate: UNSET_LABEL,
      identityIssuePlace: UNSET_LABEL,
      isIdentityVerified: true,
      pidNumber: staffCode,
    },
    vitals: {
      bloodType: UNSET_LABEL,
      heightCm: null,
      weightKg: null,
    },
    shortcuts: {
      familyMembersCount: null,
      outpatientVisitsCount: null,
      activePrescriptionsCount: null,
    },
    contact: {
      phone: UNSET_LABEL,
      isPhoneVerified: false,
      email: user.email?.trim() || UNSET_LABEL,
      isEmailVerified: true,
      permanentAddress: UNSET_LABEL,
      emergencyContact: {
        fullName: UNSET_LABEL,
        relationship: UNSET_LABEL,
        phone: UNSET_LABEL,
      },
    },
  };
}

export async function getMyPatientProfileApi(): Promise<PatientProfile> {
  const authUser = useAuthStore.getState().user;
  const isStaffOnlyAccount =
    Boolean(authUser) && isStaffRole(authUser?.roles) && !authUser?.roles?.includes('PATIENT');

  if (isStaffOnlyAccount && authUser) {
    return mapStaffUserToProfileModel(authUser);
  }

  const { data } = await apiClient.get<PatientProfileResponseDto>('/patients/me/profile');
  return mapPatientProfileDtoToModel(data);
}

export async function updateMyPatientProfileApi(
  payload: UpdateMyPatientProfileInput,
): Promise<PatientProfile> {
  const body: Record<string, unknown> = {
    phone: payload.phone,
    address: payload.address,
    bloodType: payload.bloodType,
    emergencyContact: payload.emergencyContact,
  };

  if (payload.dateOfBirth !== undefined) {
    body.dateOfBirth = payload.dateOfBirth;
  }
  if (payload.gender !== undefined) {
    body.gender = payload.gender;
  }

  const { data } = await apiClient.put<PatientProfileResponseDto>('/patients/me/profile', body);
  return mapPatientProfileDtoToModel(data);
}

export async function patchMyPatientProfileApi(
  payload: Partial<UpdateMyPatientProfileInput>,
): Promise<PatientProfile> {
  const { data } = await apiClient.patch<PatientProfileResponseDto>(
    '/patients/me/profile',
    payload,
  );
  return mapPatientProfileDtoToModel(data);
}
