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
}

export interface LoginResponse {
  accessToken: string;
  accessTokenExpiresIn: number;
  refreshToken: string;
  refreshTokenExpiresIn: number;
  user: UserDto;
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

export interface MessageResponse {
  message: string;
}
