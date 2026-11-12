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
