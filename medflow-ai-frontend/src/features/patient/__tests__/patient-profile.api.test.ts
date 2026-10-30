import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { AxiosError, type AxiosResponse } from 'axios';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import {
  getMyPatientProfileApi,
  mapPatientProfileDtoToModel,
  mapStaffUserToProfileModel,
  type PatientProfileResponseDto,
} from '../api/patient-profile.api';
import { mockPatientProfile } from '../data/patient-profile.mock';
import { usePatientProfileQuery } from '../hooks/use-patient-profile';

describe('patient-profile.api (M1-24 HTTP + fallback)', () => {
  afterEach(() => {
    useAuthStore.getState().clearAuth();
    vi.restoreAllMocks();
  });

  it('calls GET /patients/me/profile and maps backend DTO to PatientProfile model', async () => {
    const fakeDto: PatientProfileResponseDto = {
      id: 1,
      userId: 1,
      patientCode: 'PAT-0001',
      fullName: 'Nguyễn Văn An',
      dateOfBirth: '1980-02-02',
      age: 46,
      gender: 'MALE',
      phone: '0900000001',
      email: 'patient01@medflow.test',
      isEmailVerified: true,
      isIdentityVerified: true,
      maskedIdentification: '****** (Đã xác thực CCCD)',
      status: 'ACTIVE',
      address: 'Địa chỉ demo số 1, Hà Nội',
      bloodType: 'A+',
      allowedActions: [
        'UPDATE_PERSONAL_PROFILE',
        'MANAGE_FAMILY_PROFILES',
        'DOWNLOAD_PROFILE_SUMMARY',
      ],
    };

    const getSpy = vi
      .spyOn(apiClient, 'get')
      .mockResolvedValueOnce({ data: fakeDto } as AxiosResponse<PatientProfileResponseDto>);

    const result = await getMyPatientProfileApi();

    expect(getSpy).toHaveBeenCalledWith('/patients/me/profile');
    expect(result.id).toBe('PAT-0001');
    expect(result.identity.fullName).toBe('NGUYỄN VĂN AN');
    expect(result.identity.dateOfBirth).toBe('02/02/1980');
    expect(result.identity.age).toBe(46);
    expect(result.identity.gender).toBe('Nam');
    expect(result.identity.isIdentityVerified).toBe(true);
    expect(result.identity.identityNumber).toBe('****** (Đã xác thực CCCD)');
    expect(result.identity.pidNumber).toBe('PAT-0001');
    expect(result.vitals.bloodType).toBe('A+');
    expect(result.contact.phone).toBe('0900000001');
    expect(result.contact.email).toBe('patient01@medflow.test');
    expect(result.contact.isEmailVerified).toBe(true);
    expect(result.contact.permanentAddress).toBe('Địa chỉ demo số 1, Hà Nội');
  });

  it('maps FEMALE and OTHER genders and falls back when optional fields are null', () => {
    const femaleDto: PatientProfileResponseDto = {
      id: 2,
      userId: 2,
      patientCode: 'PAT-0002',
      fullName: 'Trần Thị Bình',
      dateOfBirth: null,
      age: null,
      gender: 'FEMALE',
      phone: null,
      email: null,
      isEmailVerified: false,
      isIdentityVerified: false,
      maskedIdentification: null,
      status: 'ACTIVE',
      address: null,
      bloodType: null,
      allowedActions: [],
    };

    const mapped = mapPatientProfileDtoToModel(femaleDto, mockPatientProfile);
    expect(mapped.identity.gender).toBe('Nữ');
    expect(mapped.identity.isIdentityVerified).toBe(false);
    expect(mapped.identity.identityNumber).toBe('Chưa cập nhật CCCD');
    expect(mapped.contact.isEmailVerified).toBe(false);
    expect(mapped.vitals.bloodType).toBe(mockPatientProfile.vitals.bloodType);

    const otherMapped = mapPatientProfileDtoToModel(
      { ...femaleDto, gender: 'OTHER' },
      mockPatientProfile,
    );
    expect(otherMapped.identity.gender).toBe('Khác');
  });

  it('falls back to mockPatientProfile when backend is offline or returns 404', async () => {
    const networkError = new AxiosError('Network Error', 'ERR_NETWORK');
    const notFoundError = new AxiosError('Not Found', 'ERR_BAD_REQUEST', undefined, undefined, {
      status: 404,
    } as AxiosResponse);
    const getSpy = vi
      .spyOn(apiClient, 'get')
      .mockRejectedValueOnce(networkError)
      .mockRejectedValueOnce(notFoundError);

    await expect(getMyPatientProfileApi()).resolves.toEqual(mockPatientProfile);
    await expect(getMyPatientProfileApi()).resolves.toEqual(mockPatientProfile);
    expect(getSpy).toHaveBeenCalledTimes(2);
  });

  it('rethrows unexpected server errors (500) instead of swallowing them', async () => {
    const serverError = new AxiosError(
      'Internal Server Error',
      'ERR_BAD_RESPONSE',
      undefined,
      undefined,
      { status: 500 } as AxiosResponse,
    );
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(serverError);

    await expect(getMyPatientProfileApi()).rejects.toThrow('Internal Server Error');
  });

  it('fetches remote profile on first mount even with mock initialData', async () => {
    const remoteDto: PatientProfileResponseDto = {
      id: 99,
      userId: 99,
      patientCode: 'PAT-0099',
      fullName: 'Lê Minh Chi',
      dateOfBirth: '1992-11-20',
      age: 33,
      gender: 'FEMALE',
      phone: '0909999999',
      email: 'chi@medflow.test',
      isEmailVerified: true,
      isIdentityVerified: true,
      maskedIdentification: '****** (Đã xác thực CCCD)',
      status: 'ACTIVE',
      address: 'Quận 1, TP.HCM',
      bloodType: 'B+',
      allowedActions: ['UPDATE_PERSONAL_PROFILE'],
    };

    const getSpy = vi
      .spyOn(apiClient, 'get')
      .mockResolvedValueOnce({ data: remoteDto } as AxiosResponse<PatientProfileResponseDto>);

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    const wrapper = ({ children }: { children: ReactNode }) =>
      createElement(QueryClientProvider, { client: queryClient }, children);

    const { result } = renderHook(() => usePatientProfileQuery(), { wrapper });

    await waitFor(() => {
      expect(result.current.data.id).toBe('PAT-0099');
    });

    expect(getSpy).toHaveBeenCalledWith('/patients/me/profile');
    expect(result.current.data.identity.fullName).toBe('LÊ MINH CHI');
  });

  it('maps staff/doctor user without calling /patients/me/profile (no 403)', async () => {
    useAuthStore.getState().setAuth({
      user: {
        id: 3,
        email: 'doctor01@medflow.test',
        fullName: 'BS Nguyễn Minh Đức',
        roles: ['DOCTOR'],
        patientCode: null,
        staffCode: 'STF-001',
      },
      accessToken: 'fake-doctor-jwt',
      refreshToken: 'fake-doctor-refresh',
    });

    const profile = await getMyPatientProfileApi();

    expect(profile.id).toBe('STF-001');
    expect(profile.identity.fullName).toBe('BS NGUYỄN MINH ĐỨC');
    expect(profile.identity.identityNumber).toBe('STF-001 (Bác sĩ lâm sàng)');
    expect(profile.contact.email).toBe('doctor01@medflow.test');

    const directMapped = mapStaffUserToProfileModel({
      id: 5,
      email: 'reception01@medflow.test',
      fullName: 'Lễ tân Lê Lan',
      roles: ['RECEPTION'],
      patientCode: null,
      staffCode: 'STF-003',
    });
    expect(directMapped.identity.identityNumber).toBe('STF-003 (Nhân viên tiếp đón)');
  });
});
