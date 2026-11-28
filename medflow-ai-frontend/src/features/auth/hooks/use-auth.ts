import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useSearchParams } from 'react-router-dom';

import { getValidAccessTokenForRealtime } from '@/shared/lib/axios';
import { ACCESS_TOKEN_KEY, REFRESH_TOKEN_KEY } from '@/shared/lib/auth-storage';
import { notifyAuthSessionInvalidated } from '@/shared/lib/navigation-guard-manager';

import * as authApi from '../api/auth.api';
import { useAuthStore } from '../store/auth.store';
import type {
  ForgotPasswordRequest,
  LoginRequest,
  RegisterRequest,
  ResetPasswordRequest,
  VerifyOtpRequest,
} from '../types/auth.types';
import { resolvePostLoginRedirect } from '../utils/role-landing';

/** Trang xác minh danh tính cho tài khoản đăng ký đang PENDING (chưa có UserDto đầy đủ). */
const REGISTRATION_EKYC_PATH = '/register/verify-identity';

/**
 * Đăng ký bắt buộc eKYC: backend cấp registration-scope access token nhưng KHÔNG cấp refresh
 * token, và tài khoản chưa có UserDto hoàn chỉnh — vì vậy KHÔNG gọi `setAuth` (không tạo user
 * giả, store vẫn coi là chưa đăng nhập). Chỉ ghi access token vào đúng storage key mà interceptor
 * của `apiClient` đọc, để các lời gọi /ekyc được xác thực. Refresh token cũ (nếu có) bị xoá để
 * không vô tình refresh bằng phiên của tài khoản khác.
 */
function persistRegistrationAccessToken(accessToken: string) {
  if (typeof localStorage === 'undefined') return;
  localStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
  localStorage.removeItem(REFRESH_TOKEN_KEY);
}

/** Login → lưu tokens + user → điều hướng về returnUrl hoặc '/'. */
export function useLogin() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const setAuth = useAuthStore((s) => s.setAuth);

  return useMutation({
    mutationFn: (payload: LoginRequest) => authApi.login(payload),
    onSuccess: (data) => {
      // Tài khoản PENDING quay lại đăng nhập (ví dụ đã đóng trình duyệt giữa chừng): backend trả
      // registration-scope token + requiresEkycVerification. Tiếp tục eKYC thay vì coi là đăng nhập.
      if (data.requiresEkycVerification) {
        if (data.accessToken) {
          persistRegistrationAccessToken(data.accessToken);
        }
        navigate(REGISTRATION_EKYC_PATH, { replace: true });
        return;
      }
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

/** Verify OTP → nếu bắt buộc eKYC thì sang trang xác minh, ngược lại về /login?verified=1. */
export function useVerifyOtp() {
  const navigate = useNavigate();

  return useMutation({
    mutationFn: (payload: VerifyOtpRequest) => authApi.verifyOtp(payload),
    onSuccess: (data) => {
      if (data.requiresEkycVerification) {
        if (data.accessToken) {
          persistRegistrationAccessToken(data.accessToken);
        }
        navigate(REGISTRATION_EKYC_PATH, { replace: true });
        return;
      }
      navigate('/login?verified=1');
    },
  });
}

export function useResendOtp() {
  return useMutation({
    mutationFn: (email: string) => authApi.resendOtp({ email }),
  });
}

export function useForgotPassword() {
  return useMutation({
    mutationFn: (payload: ForgotPasswordRequest) => authApi.forgotPassword(payload),
  });
}

export function useResetPassword() {
  const navigate = useNavigate();

  return useMutation({
    mutationFn: (payload: ResetPasswordRequest) => authApi.resetPassword(payload),
    onSuccess: () => navigate('/login?reset=1', { replace: true }),
  });
}

export function useChangePassword() {
  return useMutation({
    mutationFn: async (input: { currentPassword: string; newPassword: string }) => {
      // Ensure any imminent access-token rotation completes before reading the active session's refresh token.
      await getValidAccessTokenForRealtime();
      const currentRefreshToken = useAuthStore.getState().refreshToken;
      return authApi.changePassword({
        currentPassword: input.currentPassword,
        newPassword: input.newPassword,
        currentRefreshToken,
      });
    },
  });
}

/** Logout: gọi API revoke refresh token (fire-and-forget) rồi clear local. */
export function useLogout() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const clearAuth = useAuthStore((s) => s.clearAuth);

  return useMutation({
    mutationFn: async () => {
      notifyAuthSessionInvalidated();
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
