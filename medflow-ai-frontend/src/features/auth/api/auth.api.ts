import { apiClient } from '@/shared/lib/axios';

import type {
  ChangePasswordRequest,
  ChangePasswordResponse,
  ForgotPasswordRequest,
  LoginRequest,
  LoginResponse,
  MessageResponse,
  RefreshRequest,
  RefreshResponse,
  RegisterRequest,
  ResendOtpRequest,
  ResetPasswordRequest,
  UserDto,
  VerifyOtpRequest,
} from '../types/auth.types';

export async function login(payload: LoginRequest): Promise<LoginResponse> {
  const { data } = await apiClient.post<LoginResponse>('/auth/login', payload);
  return data;
}

export async function register(payload: RegisterRequest): Promise<MessageResponse> {
  const { data } = await apiClient.post<MessageResponse>('/auth/register', payload);
  return data;
}

export async function verifyOtp(payload: VerifyOtpRequest): Promise<MessageResponse> {
  const { data } = await apiClient.post<MessageResponse>('/auth/verify-otp', payload);
  return data;
}

export async function resendOtp(payload: ResendOtpRequest): Promise<MessageResponse> {
  const { data } = await apiClient.post<MessageResponse>('/auth/resend-otp', payload);
  return data;
}

export async function forgotPassword(payload: ForgotPasswordRequest): Promise<MessageResponse> {
  const { data } = await apiClient.post<MessageResponse>('/auth/forgot-password', payload);
  return data;
}

export async function resetPassword(payload: ResetPasswordRequest): Promise<MessageResponse> {
  const { data } = await apiClient.post<MessageResponse>('/auth/reset-password', payload);
  return data;
}

export async function changePassword(
  payload: ChangePasswordRequest,
): Promise<ChangePasswordResponse> {
  const { data } = await apiClient.post<ChangePasswordResponse>('/auth/change-password', payload);
  return data;
}

export async function refreshTokens(payload: RefreshRequest): Promise<RefreshResponse> {
  const { data } = await apiClient.post<RefreshResponse>('/auth/refresh', payload);
  return data;
}

export async function logout(payload: RefreshRequest): Promise<void> {
  await apiClient.post('/auth/logout', payload);
}

export async function getMe(): Promise<UserDto> {
  const { data } = await apiClient.get<UserDto>('/auth/me');
  return data;
}
