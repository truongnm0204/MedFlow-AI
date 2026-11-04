import { apiClient } from '@/shared/lib/axios';

import type {
  TransferVisitRoomRequestPayload,
  VisitCompatibleRoomsResponseDto,
  VisitRoomTransferResponseDto,
} from '../types/visit-room-transfer.types';

export async function fetchVisitCompatibleRooms(
  visitIdOrCode: string,
): Promise<VisitCompatibleRoomsResponseDto> {
  const encoded = encodeURIComponent(visitIdOrCode.trim());
  const { data } = await apiClient.get<VisitCompatibleRoomsResponseDto>(
    `/reception/visits/${encoded}/compatible-rooms`,
  );
  return data;
}

export async function transferVisitToCompatibleRoom(
  visitIdOrCode: string,
  payload: TransferVisitRoomRequestPayload,
): Promise<VisitRoomTransferResponseDto> {
  const encoded = encodeURIComponent(visitIdOrCode.trim());
  const { data } = await apiClient.post<VisitRoomTransferResponseDto>(
    `/reception/visits/${encoded}/transfer-room`,
    payload,
  );
  return data;
}
