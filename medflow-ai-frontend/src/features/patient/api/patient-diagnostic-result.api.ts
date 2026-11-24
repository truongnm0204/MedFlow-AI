import { apiClient } from '@/shared/lib/axios';

import type {
  PatientDiagnosticResultDetail,
  PatientDiagnosticResultListItem,
} from '../types/patient-diagnostic-result.types';

interface PatientDiagnosticResultDetailDto {
  orderId: number;
  orderCode: string | null;
  visitId: number | null;
  visitCode: string | null;
  patientId: number;
  patientCode: string;
  patientName: string;
  gender: string | null;
  birthYear: number | null;
  relationshipLabel: string;
  hospitalName: string | null;
  serviceName: string | null;
  orderingDoctorName: string | null;
  performingStaffName: string | null;
  performedPlaceName: string | null;
  signedAt: string | null;
  isPublishedToPatient: boolean;
  status: string;
  resultText: string | null;
  conclusion: string | null;
  reportFileUrl: string | null;
  fileName: string | null;
}

interface PatientDiagnosticResultListItemDto {
  orderId: number;
  orderCode: string | null;
  visitId: number | null;
  visitCode: string | null;
  patientId: number;
  patientCode: string;
  patientName: string;
  relationshipLabel: string;
  hospitalName: string | null;
  serviceName: string | null;
  orderingDoctorName: string | null;
  performedPlaceName: string | null;
  signedAt: string | null;
  isPublishedToPatient: boolean;
  status: string;
}

interface PatientDiagnosticResultListResponseDto {
  items: PatientDiagnosticResultListItemDto[];
  page: number;
  pageSize: number;
  totalCount: number;
}

function formatSignedAt(signedAt: string | null): string {
  if (!signedAt) return 'Chưa ký duyệt';
  const parsed = new Date(signedAt);
  if (Number.isNaN(parsed.getTime())) return signedAt;
  return parsed.toLocaleString('vi-VN');
}

function mapGenderLabel(gender: string | null): string {
  if (!gender) return 'Chưa cập nhật';
  const upper = gender.trim().toUpperCase();
  if (upper === 'MALE' || upper === 'NAM') return 'Nam';
  if (upper === 'FEMALE' || upper === 'NỮ' || upper === 'NU') return 'Nữ';
  return gender;
}

export async function fetchPatientDiagnosticResultDetail(
  idOrCode: string,
): Promise<PatientDiagnosticResultDetail> {
  const { data } = await apiClient.get<PatientDiagnosticResultDetailDto>(
    `/patients/me/diagnostic-results/${encodeURIComponent(idOrCode.trim())}`,
  );
  const resolvedCode = data.orderCode || `ORD-${data.orderId}`;

  return {
    orderId: data.orderId,
    orderCode: resolvedCode,
    visitId: data.visitId,
    visitCode: data.visitCode || 'Chưa cập nhật',
    patientId: data.patientId,
    patientCode: data.patientCode,
    patientName: data.patientName,
    gender: mapGenderLabel(data.gender),
    birthYear: data.birthYear,
    relationshipLabel: data.relationshipLabel,
    hospitalName: data.hospitalName || 'Bệnh viện MedFlow',
    serviceName: data.serviceName || 'Dịch vụ Cận lâm sàng',
    orderingDoctor: data.orderingDoctorName || 'Chưa cập nhật',
    performingDoctor: data.performingStaffName || 'Chưa cập nhật',
    performedRoom: data.performedPlaceName || 'Khoa Cận lâm sàng',
    signedAt: formatSignedAt(data.signedAt),
    isPublishedToPatient: data.isPublishedToPatient,
    status: data.status,
    resultText: data.resultText,
    conclusion: data.conclusion,
    reportFileUrl: data.reportFileUrl,
    fileName: data.fileName || `KQ-CLS-${resolvedCode}.pdf`,
  };
}

export async function fetchPatientDiagnosticResults(): Promise<PatientDiagnosticResultListItem[]> {
  const { data } = await apiClient.get<PatientDiagnosticResultListResponseDto>(
    '/patients/me/diagnostic-results',
  );
  return (data.items ?? []).map((item) => ({
    orderId: item.orderId,
    orderCode: item.orderCode || `ORD-${item.orderId}`,
    visitId: item.visitId,
    visitCode: item.visitCode || 'Chưa cập nhật',
    patientId: item.patientId,
    patientCode: item.patientCode,
    patientName: item.patientName,
    relationshipLabel: item.relationshipLabel,
    hospitalName: item.hospitalName || 'Bệnh viện MedFlow',
    serviceName: item.serviceName || 'Dịch vụ Cận lâm sàng',
    orderingDoctorName: item.orderingDoctorName || 'Chưa cập nhật',
    performedPlaceName: item.performedPlaceName || 'Khoa Cận lâm sàng',
    signedAt: formatSignedAt(item.signedAt),
    isPublishedToPatient: item.isPublishedToPatient,
    status: item.status,
  }));
}
