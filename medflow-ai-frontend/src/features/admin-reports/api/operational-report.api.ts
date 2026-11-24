import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import type {
  DepartmentVisitItem,
  HourlyIntakePoint,
  OperationalReportItem,
  VisitStatusSegment,
} from '../types/operational-report.types';

export interface OperationalReportSummary {
  totalVisits: number;
  totalAppointments: number;
  completedVisits: number;
  inProgressVisits: number;
  waitingVisits: number;
  walkInVisits: number;
  noShowAppointments: number;
  completionRatePercent: number;
  averageWaitMinutes: number | null;
  medianWaitMinutes: number | null;
}

export interface OperationalReportDepartmentOption {
  id: number;
  code: string;
  name: string;
}

export interface OperationalReportQueryParams {
  fromDate?: string;
  toDate?: string;
  departmentId?: number;
  serviceId?: number;
  hospitalId?: number;
}

export interface OperationalReportData {
  hospitalId: number | null;
  facilityCode: string;
  facilityName: string;
  fromDate: string;
  toDate: string;
  periodLabel: string;
  summary: OperationalReportSummary;
  hourlyIntake: HourlyIntakePoint[];
  departmentVisits: DepartmentVisitItem[];
  visitStatusSegments: VisitStatusSegment[];
  reports: OperationalReportItem[];
  availableDepartments: OperationalReportDepartmentOption[];
}

interface OperationalReportApiResponseDto {
  hospitalId: number | null;
  facilityCode: string;
  facilityName: string;
  fromDate: string;
  toDate: string;
  periodLabel: string;
  summary: OperationalReportSummary;
  hourlyIntake: Array<{ hour: string; count: number; isPeak: boolean }>;
  departmentVisits: Array<{
    departmentId: number | null;
    departmentCode: string;
    departmentName: string;
    count: number;
  }>;
  visitStatusSegments: Array<{
    key: 'COMPLETED' | 'IN_PROGRESS' | 'WAITING';
    label: string;
    count: number;
    percentValue: number;
  }>;
  departmentRows: Array<{
    id: string;
    code: string;
    name: string;
    departmentId: number | null;
    departmentName: string;
    facilityName: string;
    periodLabel: string;
    totalVisits: number;
    completedVisits: number;
    walkInVisits: number;
    totalAppointments: number;
    noShowAppointments: number;
    averageWaitMinutes: number | null;
    medianWaitMinutes: number | null;
    status: 'COMPLETED' | 'NEEDS_REFRESH' | 'NEEDS_REVIEW';
  }>;
  availableDepartments: OperationalReportDepartmentOption[];
}

interface ProblemDetailsBody {
  detail?: string;
  title?: string;
  message?: string;
}

function extractReportApiError(error: unknown): Error {
  if (axios.isAxiosError(error)) {
    const body = error.response?.data as ProblemDetailsBody | undefined;
    const msg = body?.detail || body?.message || body?.title;
    if (msg && msg.trim().length > 0) return new Error(msg.trim());
  }
  if (error instanceof Error) return error;
  return new Error('Không thể tải dữ liệu báo cáo vận hành từ máy chủ.');
}

function resolveSegmentColors(key: 'COMPLETED' | 'IN_PROGRESS' | 'WAITING') {
  if (key === 'COMPLETED') {
    return { barColorClass: 'bg-primary', swatchColorClass: 'bg-primary' };
  }
  if (key === 'IN_PROGRESS') {
    return { barColorClass: 'bg-sky-400', swatchColorClass: 'bg-sky-400' };
  }
  return { barColorClass: 'bg-amber-400', swatchColorClass: 'bg-amber-400' };
}

export async function fetchOperationalReportApi(
  params?: OperationalReportQueryParams,
): Promise<OperationalReportData> {
  try {
    const queryParams: Record<string, string | number> = {};
    if (params?.fromDate) queryParams.fromDate = params.fromDate;
    if (params?.toDate) queryParams.toDate = params.toDate;
    if (params?.departmentId) queryParams.departmentId = params.departmentId;
    if (params?.serviceId) queryParams.serviceId = params.serviceId;
    if (params?.hospitalId) queryParams.hospitalId = params.hospitalId;

    const { data } = await apiClient.get<OperationalReportApiResponseDto>(
      '/admin/reports/operational',
      { params: queryParams },
    );

    const reports: OperationalReportItem[] = (data.departmentRows ?? []).map((row) => ({
      id: row.id,
      code: row.code,
      name: row.name,
      periodLabel: row.periodLabel,
      category: row.departmentName,
      facility: row.facilityName,
      intakeVolumeLabel: `${row.totalVisits} lượt (${row.walkInVisits} vãng lai)`,
      medianWaitLabel: row.medianWaitMinutes != null ? `${row.medianWaitMinutes} phút` : '—',
      aggregationStatus: 'AGGREGATED',
      status: row.status,
    }));

    const visitStatusSegments: VisitStatusSegment[] = (data.visitStatusSegments ?? []).map(
      (seg) => {
        const colors = resolveSegmentColors(seg.key);
        return {
          key: seg.key,
          label: seg.label,
          countLabel: `${seg.count} lượt`,
          percentLabel: `${seg.percentValue}%`,
          percentValue: seg.percentValue,
          barColorClass: colors.barColorClass,
          swatchColorClass: colors.swatchColorClass,
        };
      },
    );

    return {
      hospitalId: data.hospitalId,
      facilityCode: data.facilityCode,
      facilityName: data.facilityName,
      fromDate: data.fromDate,
      toDate: data.toDate,
      periodLabel: data.periodLabel,
      summary: data.summary,
      hourlyIntake: data.hourlyIntake ?? [],
      departmentVisits: (data.departmentVisits ?? []).map((d) => ({
        department: d.departmentName,
        count: d.count,
      })),
      visitStatusSegments,
      reports,
      availableDepartments: data.availableDepartments ?? [],
    };
  } catch (error) {
    throw extractReportApiError(error);
  }
}
