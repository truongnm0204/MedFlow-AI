// Types khớp DTOs backend `MedFlow.Application/DTOs/Auth`.

export interface LoginRequest {
  identifier: string;
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
  id: string;
  username: string;
  email: string;
  fullName: string | null;
  roles: string[];
  patientCode: string | null;
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
