// Types khớp DTOs backend `MedFlow.Application/DTOs/Auth`.

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  confirmPassword: string;
  fullName: string;
  dateOfBirth: string; // yyyy-MM-dd → DateOnly
  gender: 'MALE' | 'FEMALE' | 'OTHER';
  phone: string;
}

export interface UserDto {
  /** numeric — backend emits bigint PK as JSON number (v4.1). */
  id: number;
  email: string;
  fullName: string | null;
  roles: string[];
  patientCode: string | null;
  staffCode?: string | null;
  permissions?: string[];
}

export interface LoginResponse {
  accessToken: string;
  accessTokenExpiresIn: number;
  refreshToken: string;
  refreshTokenExpiresIn: number;
  user: UserDto;
  /**
   * `true` khi tài khoản đang PENDING (đăng ký bắt buộc eKYC): backend trả registration-scope
   * access token, KHÔNG cấp refresh token — người dùng phải hoàn tất eKYC trước khi đăng nhập thật.
   */
  requiresEkycVerification: boolean;
}

/**
 * `POST /auth/verify-otp` response.
 * Khi bật cờ đăng ký bắt buộc eKYC, tài khoản vẫn PENDING và backend cấp một registration-scope
 * access token (không có refresh token) để gọi các endpoint eKYC.
 */
export interface VerifyOtpResponse {
  userId: number;
  message: string;
  accessToken?: string | null;
  accessTokenExpiresIn?: number | null;
  requiresEkycVerification: boolean;
}

export interface RefreshRequest {
  refreshToken: string;
}

export interface RefreshResponse {
  accessToken: string;
  accessTokenExpiresIn: number;
  refreshToken: string;
  refreshTokenExpiresIn: number;
}

export interface VerifyOtpRequest {
  email: string;
  otp: string;
}

export interface ResendOtpRequest {
  email: string;
}

export interface ForgotPasswordRequest {
  email: string;
}

export interface ResetPasswordRequest {
  email: string;
  otpCode: string;
  newPassword: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
  currentRefreshToken?: string | null;
}

export interface ChangePasswordResponse {
  message: string;
  keptCurrentSession: boolean;
}

export interface MessageResponse {
  message: string;
}
