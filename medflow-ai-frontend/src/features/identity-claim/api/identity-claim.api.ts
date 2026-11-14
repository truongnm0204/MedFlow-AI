import { apiClient } from '@/shared/lib/axios';

import type {
  CreateIdentityClaimPayload,
  IdentityClaim,
  IdentityHospital,
  StaffIdentityClaim,
} from '../types/identity-claim.types';

interface HospitalDto {
  id: number;
  name?: string | null;
  isActive?: boolean | null;
}

export async function fetchIdentityHospitalsApi(): Promise<IdentityHospital[]> {
  const { data } = await apiClient.get<HospitalDto[]>('/hospitals');
  return data.map((hospital) => ({
    id: hospital.id,
    name: hospital.name?.trim() || 'Cơ sở chưa đặt tên',
    isActive: hospital.isActive !== false,
  }));
}

export async function sendIdentityClaimOtpApi(): Promise<{ expiresAt: string }> {
  const { data } = await apiClient.post<{ expiresAt: string }>('/patients/me/identity-claims/otp');
  return data;
}

export async function createIdentityClaimApi(
  payload: CreateIdentityClaimPayload,
): Promise<IdentityClaim> {
  const { data } = await apiClient.post<IdentityClaim>('/patients/me/identity-claims', payload);
  return data;
}

export async function fetchMyIdentityClaimsApi(): Promise<IdentityClaim[]> {
  const { data } = await apiClient.get<IdentityClaim[]>('/patients/me/identity-claims');
  return data;
}

export async function fetchIdentityClaimApi(claimId: number): Promise<IdentityClaim> {
  const { data } = await apiClient.get<IdentityClaim>(`/patients/me/identity-claims/${claimId}`);
  return data;
}

export async function fetchIdentityClaimQueueApi(status?: string): Promise<StaffIdentityClaim[]> {
  const { data } = await apiClient.get<StaffIdentityClaim[]>('/reception/patient-identity-claims', {
    params: status ? { status } : undefined,
  });
  return data;
}

export async function approveIdentityClaimApi(claimId: number): Promise<IdentityClaim> {
  const { data } = await apiClient.post<IdentityClaim>(
    `/reception/patient-identity-claims/${claimId}/approve`,
  );
  return data;
}

export async function rejectIdentityClaimApi(
  claimId: number,
  reason: string,
): Promise<IdentityClaim> {
  const { data } = await apiClient.post<IdentityClaim>(
    `/reception/patient-identity-claims/${claimId}/reject`,
    { reason },
  );
  return data;
}
