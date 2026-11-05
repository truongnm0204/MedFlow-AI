import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  fetchVisitCompatibleRooms,
  transferVisitToCompatibleRoom,
} from '../api/visit-room-transfer.api';
import type {
  TransferVisitRoomRequestPayload,
  VisitCompatibleRoomsResponseDto,
  VisitRoomTransferResponseDto,
} from '../types/visit-room-transfer.types';

export interface TransferVisitRoomVariables {
  visitIdOrCode: string;
  payload: TransferVisitRoomRequestPayload;
}

export function useVisitCompatibleRooms(visitIdOrCode: string | null, enabled = true) {
  const trimmed = visitIdOrCode?.trim() ?? '';
  return useQuery<VisitCompatibleRoomsResponseDto>({
    queryKey: ['visit-compatible-rooms', trimmed],
    queryFn: () => fetchVisitCompatibleRooms(trimmed),
    enabled: enabled && trimmed.length > 0,
    retry: false,
  });
}

export function useTransferVisitRoom() {
  const queryClient = useQueryClient();

  return useMutation<VisitRoomTransferResponseDto, unknown, TransferVisitRoomVariables>({
    mutationFn: ({ visitIdOrCode, payload }) =>
      transferVisitToCompatibleRoom(visitIdOrCode, payload),
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({
        queryKey: ['visit-compatible-rooms', variables.visitIdOrCode.trim()],
      });
    },
  });
}
