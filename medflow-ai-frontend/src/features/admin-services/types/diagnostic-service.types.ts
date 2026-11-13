export type DiagnosticServiceStatus = 'ACTIVE' | 'INACTIVE';

export interface DiagnosticServicePoint {
  servicePointId: number;
  name: string;
  roomId: number | null;
  roomCode: string | null;
  roomName: string | null;
  isActive: boolean;
}

export interface DiagnosticServiceItem {
  id: number;
  serviceCode: string;
  name: string;
  description: string | null;
  serviceType: string;
  departmentId: number | null;
  departmentCode: string | null;
  departmentName: string | null;
  specialtyId: number | null;
  specialtyCode: string | null;
  specialtyName: string | null;
  price: number | null;
  priceFormatted: string | null;
  requiresResultVerification: boolean;
  isActive: boolean;
  status: DiagnosticServiceStatus;
  servicePointCount: number;
  activeServicePointCount: number;
  servicePoints: DiagnosticServicePoint[];
}

export interface DiagnosticServiceMetrics {
  total: number;
  active: number;
  inactive: number;
  requiresVerificationCount: number;
  departmentsCount: number;
  activeServicePointsCount: number;
}

export interface DiagnosticServiceListResponse {
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  data: DiagnosticServiceItem[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
  metrics: DiagnosticServiceMetrics;
}

export interface DiagnosticServiceListParams {
  keyword?: string;
  departmentId?: number;
  status?: DiagnosticServiceStatus;
  requiresResultVerification?: boolean;
  page: number;
  pageSize: number;
}

export interface DiagnosticServiceDepartmentOption {
  id: number;
  name: string;
}

export interface DiagnosticServiceFilterValue {
  keyword: string;
  departmentId?: number;
  status?: DiagnosticServiceStatus;
  requiresResultVerification?: boolean;
}

export interface DiagnosticServiceDepartmentFormOption {
  id: number;
  departmentCode: string | null;
  name: string;
}

export interface DiagnosticServiceSpecialtyFormOption {
  id: number;
  specialtyCode: string | null;
  name: string;
}

export interface DiagnosticServiceFormOptions {
  hospitalId: number;
  departments: DiagnosticServiceDepartmentFormOption[];
  specialties: DiagnosticServiceSpecialtyFormOption[];
}

export interface CreateDiagnosticServicePayload {
  serviceCode: string;
  name: string;
  description: string | null;
  departmentId: number;
  specialtyId: number | null;
  requiresResultVerification: boolean;
  price: number | null;
}

export interface UpdateDiagnosticServicePayload {
  name: string;
  description: string | null;
  departmentId: number;
  specialtyId: number | null;
  requiresResultVerification: boolean;
}

export interface DeactivateDiagnosticServicePayload {
  reason: string;
}
