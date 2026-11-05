import { apiClient } from '@/shared/lib/axios';

import type {
  AssignExaminationRoomPayload,
  AssignExaminationRoomResponseDto,
  CompatibleExaminationRoomListResponseDto,
} from '../types/assign-examination-room.types';

export async function fetchCompatibleExaminationRooms(
  visitIdOrCode: string,
): Promise<CompatibleExaminationRoomListResponseDto> {
  const encoded = encodeURIComponent(visitIdOrCode.trim());
  const { data } = await apiClient.get<CompatibleExaminationRoomListResponseDto>(
    `/reception/visits/${encoded}/available-rooms`,
  );
  return data;
}

export async function assignExaminationRoom(
  visitIdOrCode: string,
  payload: AssignExaminationRoomPayload,
): Promise<AssignExaminationRoomResponseDto> {
  const encoded = encodeURIComponent(visitIdOrCode.trim());
  const { data } = await apiClient.put<AssignExaminationRoomResponseDto>(
    `/reception/visits/${encoded}/examination-room`,
    payload,
  );
  return data;
}
