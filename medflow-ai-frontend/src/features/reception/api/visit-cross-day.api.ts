import { apiClient } from '@/shared/lib/axios';

import type {
  DeferVisitOrOrdersRequestDto,
  DeferVisitOrOrdersResponseDto,
  ResumeCrossDayVisitRequestDto,
  ResumeCrossDayVisitResponseDto,
  VisitResumeContextResponseDto,
} from '../types/visit-cross-day.types';

export interface AssignableDoctorOptionDto {
  doctorId: number;
  doctorCode: string;
  fullName: string;
  roomCode: string | null;
  roomName: string | null;
  departmentName: string | null;
}

export async function fetchReceptionVisitResumeContextApi(
  visitIdOrCode: string,
): Promise<VisitResumeContextResponseDto> {
  const encoded = encodeURIComponent(visitIdOrCode.trim());
  const { data } = await apiClient.get<VisitResumeContextResponseDto>(
    `/reception/visits/${encoded}/resume-context`,
  );
  return data;
}

export async function resumeReceptionCrossDayVisitApi(
  visitIdOrCode: string,
  payload: ResumeCrossDayVisitRequestDto,
): Promise<ResumeCrossDayVisitResponseDto> {
  const encoded = encodeURIComponent(visitIdOrCode.trim());
  const { data } = await apiClient.post<ResumeCrossDayVisitResponseDto>(
    `/reception/visits/${encoded}/resume`,
    payload,
  );
  return data;
}

export async function deferReceptionVisitOrOrdersApi(
  visitIdOrCode: string,
  payload: DeferVisitOrOrdersRequestDto,
): Promise<DeferVisitOrOrdersResponseDto> {
  const encoded = encodeURIComponent(visitIdOrCode.trim());
  const { data } = await apiClient.post<DeferVisitOrOrdersResponseDto>(
    `/reception/visits/${encoded}/defer`,
    payload,
  );
  return data;
}

export async function fetchAssignableDoctorsForResumeApi(
  visitIdOrCode: string,
): Promise<AssignableDoctorOptionDto[]> {
  const encoded = encodeURIComponent(visitIdOrCode.trim());
  const { data } = await apiClient.get<{
    doctors?: Array<{
      doctorId?: number;
      id?: number;
      doctorCode?: string;
      fullName?: string;
      displayName?: string;
      roomCode?: string | null;
      roomName?: string | null;
      departmentName?: string | null;
    }>;
    items?: Array<{
      doctorId?: number;
      id?: number;
      doctorCode?: string;
      fullName?: string;
      displayName?: string;
      roomCode?: string | null;
      roomName?: string | null;
      departmentName?: string | null;
    }>;
  }>(`/reception/visits/${encoded}/assignable-doctors`);

  const list = data.doctors ?? data.items ?? [];
  return list
    .map((item) => {
      const doctorId = item.doctorId ?? item.id ?? 0;
      return {
        doctorId,
        doctorCode: item.doctorCode ?? `DOC-${doctorId}`,
        fullName: item.displayName ?? item.fullName ?? `Bác sĩ #${doctorId}`,
        roomCode: item.roomCode ?? null,
        roomName: item.roomName ?? null,
        departmentName: item.departmentName ?? null,
      };
    })
    .filter((d) => d.doctorId > 0);
}
