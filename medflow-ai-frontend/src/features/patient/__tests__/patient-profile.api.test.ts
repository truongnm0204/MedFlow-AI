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
  patchMyPatientProfileApi,
  updateMyPatientProfileApi,
  type PatientProfileResponseDto,
} from '../api/patient-profile.api';
import { usePatientProfileQuery } from '../hooks/use-patient-profile';

describe('patient-profile.api (PHI safety & strict HTTP)', () => {
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
      allowedActions: ['UPDATE_PERSONAL_PROFILE'],
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
    expect(result.vitals.bloodType).toBe('A+');
    expect(result.vitals.heightCm).toBeNull();
    expect(result.vitals.weightKg).toBeNull();
    expect(result.contact.phone).toBe('0900000001');
  });

  it('never leaks mock O+ blood type, height, weight, or fake emergency contact when DTO fields are null', () => {
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

    const mapped = mapPatientProfileDtoToModel(femaleDto);
    expect(mapped.identity.gender).toBe('Nữ');
    expect(mapped.identity.isIdentityVerified).toBe(false);
    expect(mapped.identity.identityNumber).toBe('Chưa cập nhật CCCD');
    expect(mapped.vitals.bloodType).toBe('Chưa cập nhật');
    expect(mapped.vitals.bloodType).not.toBe('O+');
    expect(mapped.vitals.heightCm).toBeNull();
    expect(mapped.vitals.weightKg).toBeNull();
    expect(mapped.contact.emergencyContact.fullName).toBe('Chưa cập nhật');

    const otherMapped = mapPatientProfileDtoToModel({ ...femaleDto, gender: 'OTHER' });
    expect(otherMapped.identity.gender).toBe('Khác');
  });

  it('propagates 401, 404, and network errors instead of falling back to mockPatientProfile', async () => {
    const unauthorizedError = new AxiosError(
      'Unauthorized',
      'ERR_BAD_REQUEST',
      undefined,
      undefined,
      { status: 401 } as AxiosResponse,
    );
    const notFoundError = new AxiosError('Not Found', 'ERR_BAD_REQUEST', undefined, undefined, {
      status: 404,
    } as AxiosResponse);
    const networkError = new AxiosError('Network Error', 'ERR_NETWORK');

    vi.spyOn(apiClient, 'get')
      .mockRejectedValueOnce(unauthorizedError)
      .mockRejectedValueOnce(notFoundError)
      .mockRejectedValueOnce(networkError);

    await expect(getMyPatientProfileApi()).rejects.toThrow('Unauthorized');
    await expect(getMyPatientProfileApi()).rejects.toThrow('Not Found');
    await expect(getMyPatientProfileApi()).rejects.toThrow('Network Error');
  });

  it('fetches remote profile via usePatientProfileQuery without mock initialData', async () => {
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
    expect(result.current.data).toBeUndefined();

    await waitFor(() => {
      expect(result.current.data?.id).toBe('PAT-0099');
    });

    expect(getSpy).toHaveBeenCalledWith('/patients/me/profile');
    expect(result.current.data?.identity.fullName).toBe('LÊ MINH CHI');
  });

  it('maps staff/doctor user without calling /patients/me/profile or leaking O+ blood type', async () => {
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
    expect(profile.vitals.bloodType).toBe('Chưa cập nhật');

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

  it('calls PUT or PATCH /patients/me/profile and maps persisted emergencyContact', async () => {
    const updatedDto: PatientProfileResponseDto = {
      id: 1,
      userId: 1,
      patientCode: 'PAT-0001',
      fullName: 'Nguyễn Văn An',
      dateOfBirth: '1980-02-02',
      age: 46,
      gender: 'MALE',
      phone: '0988776655',
      email: 'patient01@medflow.test',
      isEmailVerified: true,
      isIdentityVerified: true,
      maskedIdentification: '****** (Đã xác thực CCCD)',
      status: 'ACTIVE',
      address: '12 Trần Hưng Đạo, Hà Nội',
      bloodType: 'O-',
      allowedActions: ['UPDATE_PERSONAL_PROFILE'],
      emergencyContact: {
        fullName: 'Nguyễn Thị Hoa',
        relationship: 'Vợ',
        phone: '0911223344',
      },
    };

    const putSpy = vi
      .spyOn(apiClient, 'put')
      .mockResolvedValueOnce({ data: updatedDto } as AxiosResponse<PatientProfileResponseDto>);
    const patchSpy = vi
      .spyOn(apiClient, 'patch')
      .mockResolvedValueOnce({ data: updatedDto } as AxiosResponse<PatientProfileResponseDto>);

    const putModel = await updateMyPatientProfileApi({
      phone: '0988776655',
      address: '12 Trần Hưng Đạo, Hà Nội',
      bloodType: 'O-',
      emergencyContact: {
        fullName: 'Nguyễn Thị Hoa',
        relationship: 'Vợ',
        phone: '0911223344',
      },
    });

    expect(putSpy).toHaveBeenCalledWith('/patients/me/profile', {
      phone: '0988776655',
      address: '12 Trần Hưng Đạo, Hà Nội',
      bloodType: 'O-',
      emergencyContact: {
        fullName: 'Nguyễn Thị Hoa',
        relationship: 'Vợ',
        phone: '0911223344',
      },
    });
    expect(putModel.vitals.bloodType).toBe('O-');
    expect(putModel.contact.emergencyContact).toEqual({
      fullName: 'Nguyễn Thị Hoa',
      relationship: 'Vợ',
      phone: '0911223344',
    });

    await patchMyPatientProfileApi({ address: null });
    expect(patchSpy).toHaveBeenCalledWith('/patients/me/profile', { address: null });
  });
});
