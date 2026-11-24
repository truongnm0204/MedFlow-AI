import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

export interface DiagnosticResultViewDocumentDto {
  reportFileUrl: string | null;
  hasDocument: boolean;
  fileName: string | null;
}

export interface DiagnosticResultViewPatientDto {
  patientId: number;
  patientCode: string;
  fullName: string;
  dateOfBirth: string | null;
  age: number | null;
  gender: string | null;
  phone: string | null;
  bloodType: string | null;
  allergies: string | null;
}

export interface DiagnosticResultViewClinicalContextDto {
  visitId: number;
  visitCode: string;
  visitStatus: string;
  reasonForVisit: string | null;
  presentingComplaint: string | null;
  clinicalFindings: string | null;
  primaryDiagnosisCode: string | null;
  primaryDiagnosisName: string | null;
  clinicalDiagnosis: string | null;
}

export interface DiagnosticResultViewVersionDto {
  versionId: number;
  resultId: number;
  versionNo: number;
  resultText: string | null;
  conclusion: string | null;
  reportFileUrl: string | null;
  isFinalized: boolean;
  enteredByStaffName: string | null;
  enteredAt: string | null;
  verifiedByStaffName: string | null;
  verifiedAt: string | null;
  correctionReason: string | null;
}

export interface DiagnosticResultViewItemDto {
  orderId: number;
  orderCode: string;
  visitId: number;
  visitCode: string;
  visitStatus: string;
  patient: DiagnosticResultViewPatientDto;
  orderingDoctorName: string;
  serviceId: number;
  serviceCode: string;
  serviceName: string;
  category: string;
  unitPrice: number;
  priority: string;
  orderStatus: string;
  departmentName: string;
  roomCode: string | null;
  roomName: string | null;
  servicePointName: string | null;
  instruction: string | null;
  orderedAt: string;
  completedAt: string | null;
  resultId: number | null;
  resultStatus: string;
  isResultFinalized: boolean;
  resultFinalizedAt: string | null;
  finalizedByStaffName: string | null;
  latestVersionNo: number | null;
  resultText: string | null;
  conclusion: string | null;
  document: DiagnosticResultDocumentDtoCompat;
  enteredByStaffName: string | null;
  enteredAt: string | null;
  verifiedByStaffName: string | null;
  verifiedAt: string | null;
  correctionReason: string | null;
}

export type DiagnosticResultDocumentDtoCompat = DiagnosticResultViewDocumentDto;

export interface DiagnosticResultViewDetailResponseDto {
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  generatedAtUtc: string;
  item: DiagnosticResultViewItemDto;
  clinicalContext: DiagnosticResultViewClinicalContextDto;
  versions: DiagnosticResultViewVersionDto[];
}

export interface DiagnosticResultViewListResponseDto {
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  departmentId: number | null;
  departmentName: string;
  items: DiagnosticResultViewItemDto[];
}

interface ProblemDetailsBody {
  detail?: string;
  title?: string;
  message?: string;
}

function extractDiagnosticViewError(error: unknown, fallbackMessage: string): Error {
  if (axios.isAxiosError(error)) {
    const body = error.response?.data as ProblemDetailsBody | undefined;
    const msg = body?.detail || body?.message || body?.title;
    if (msg && msg.trim().length > 0) {
      return new Error(msg.trim());
    }
  }
  if (error instanceof Error) return error;
  return new Error(fallbackMessage);
}

export async function fetchDiagnosticResultViewDetail(
  idOrCode: string,
): Promise<DiagnosticResultViewDetailResponseDto> {
  const trimmed = idOrCode.trim();
  if (!trimmed) {
    throw new Error('Mã chỉ định cận lâm sàng không được để trống.');
  }

  try {
    const { data } = await apiClient.get<DiagnosticResultViewDetailResponseDto>(
      `/diagnostic/results/${encodeURIComponent(trimmed)}/view`,
    );
    return data;
  } catch (error) {
    throw extractDiagnosticViewError(
      error,
      'Không tìm thấy phiếu kết quả cận lâm sàng hoặc bạn không có quyền xem.',
    );
  }
}

export async function fetchDiagnosticResultViewsList(): Promise<DiagnosticResultViewListResponseDto> {
  try {
    const { data } = await apiClient.get<DiagnosticResultViewListResponseDto>(
      '/diagnostic/results/view',
    );
    return data;
  } catch (error) {
    throw extractDiagnosticViewError(
      error,
      'Không thể tải danh sách phiếu kết quả cận lâm sàng đã lưu.',
    );
  }
}
