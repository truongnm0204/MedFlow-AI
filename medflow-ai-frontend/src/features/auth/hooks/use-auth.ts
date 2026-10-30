import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useSearchParams } from 'react-router-dom';

import * as authApi from '../api/auth.api';
import { useAuthStore } from '../store/auth.store';
import type { LoginRequest, RegisterRequest, VerifyOtpRequest } from '../types/auth.types';
import { resolvePostLoginRedirect } from '../utils/role-landing';

/** Login → lưu tokens + user → điều hướng về returnUrl hoặc '/'. */
export function useLogin() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const setAuth = useAuthStore((s) => s.setAuth);

  return useMutation({
    mutationFn: (payload: LoginRequest) => authApi.login(payload),
    onSuccess: (data) => {
      setAuth({
        user: data.user,
        accessToken: data.accessToken,
        refreshToken: data.refreshToken,
      });
      const target = resolvePostLoginRedirect(params.get('returnUrl'), data.user.roles);
      navigate(target, { replace: true });
    },
  });
}

/** Register → điều hướng sang /verify-otp kèm email. */
export function useRegister() {
  const navigate = useNavigate();

  return useMutation({
    mutationFn: (payload: RegisterRequest) => authApi.register(payload),
    onSuccess: (_data, variables) => {
      navigate(`/verify-otp?email=${encodeURIComponent(variables.email)}`);
    },
  });
}

/** Verify OTP → điều hướng về /login?verified=1. */
export function useVerifyOtp() {
  const navigate = useNavigate();

  return useMutation({
    mutationFn: (payload: VerifyOtpRequest) => authApi.verifyOtp(payload),
    onSuccess: () => navigate('/login?verified=1'),
  });
}

export function useResendOtp() {
  return useMutation({
    mutationFn: (email: string) => authApi.resendOtp({ email }),
  });
}

/** Logout: gọi API revoke refresh token (fire-and-forget) rồi clear local. */
export function useLogout() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const clearAuth = useAuthStore((s) => s.clearAuth);

  return useMutation({
    mutationFn: async () => {
      // Đọc token tại thời điểm mutate — tránh token cũ sau silent refresh rotate.
      const refreshToken = useAuthStore.getState().refreshToken;
      if (refreshToken) {
        try {
          await authApi.logout({ refreshToken });
        } catch {
          // Bỏ qua lỗi API — logout local vẫn phải xảy ra.
        }
      }
    },
    onSettled: () => {
      clearAuth();
      queryClient.clear();
      navigate('/', { replace: true });
    },
  });
}

/** Lấy profile hiện tại; chỉ chạy khi đã có access token. */
export function useMe() {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const setUser = useAuthStore((s) => s.setUser);
  return useQuery({
    queryKey: ['auth', 'me'],
    queryFn: async () => {
      const profile = await authApi.getMe();
      setUser(profile);
      return profile;
    },
    enabled: isAuthenticated,
    staleTime: 60_000,
  });
}
