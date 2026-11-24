import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { createAdminRoomApi, fetchAdminRoomsApi, updateAdminRoomApi } from '../api/room.api';

export const adminRoomsQueryKey = ['admin', 'rooms'] as const;

export function useAdminRooms() {
  return useQuery({
    queryKey: adminRoomsQueryKey,
    queryFn: fetchAdminRoomsApi,
  });
}

export function useCreateRoomMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createAdminRoomApi,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminRoomsQueryKey });
    },
  });
}

export function useUpdateRoomMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      payload,
    }: {
      id: string | number;
      payload: Parameters<typeof updateAdminRoomApi>[1];
    }) => updateAdminRoomApi(id, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminRoomsQueryKey });
    },
  });
}
