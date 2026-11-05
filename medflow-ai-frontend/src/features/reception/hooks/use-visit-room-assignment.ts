import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  assignExaminationRoom,
  fetchCompatibleExaminationRooms,
} from '../api/visit-room-assignment.api';
import type {
  AssignExaminationRoomPayload,
  AssignExaminationRoomResponseDto,
  CompatibleExaminationRoomListResponseDto,
} from '../types/assign-examination-room.types';

export interface AssignExaminationRoomVariables {
  visitIdOrCode: string;
  payload: AssignExaminationRoomPayload;
}

export function useCompatibleExaminationRooms(visitIdOrCode: string | null, enabled = true) {
  const trimmed = visitIdOrCode?.trim() ?? '';
  return useQuery<CompatibleExaminationRoomListResponseDto>({
    queryKey: ['visit-compatible-rooms', trimmed],
    queryFn: () => fetchCompatibleExaminationRooms(trimmed),
    enabled: enabled && trimmed.length > 0,
    retry: false,
  });
}

export function useAssignExaminationRoom() {
  const queryClient = useQueryClient();

  return useMutation<AssignExaminationRoomResponseDto, unknown, AssignExaminationRoomVariables>({
    mutationFn: ({ visitIdOrCode, payload }) => assignExaminationRoom(visitIdOrCode, payload),
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({
        queryKey: ['visit-compatible-rooms', variables.visitIdOrCode.trim()],
      });
    },
  });
}
