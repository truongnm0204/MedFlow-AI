import { useEffect } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  createAdminExaminationSlotApi,
  fetchAdminExaminationSlotsApi,
  updateAdminExaminationSlotApi,
} from '../api/examination-slot.api';
import type { ExaminationSlotFormValues } from '../schemas/examination-slot.schema';
import { useExaminationSlotStore } from '../store/examination-slot.store';

export const adminExaminationSlotsQueryKey = ['admin', 'examination-slots'] as const;

export function useAdminExaminationSlots() {
  const query = useQuery({
    queryKey: adminExaminationSlotsQueryKey,
    queryFn: fetchAdminExaminationSlotsApi,
  });

  useEffect(() => {
    if (query.data) {
      useExaminationSlotStore.setState({ slots: query.data });
    }
  }, [query.data]);

  return query;
}

export function useCreateExaminationSlotMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createAdminExaminationSlotApi,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminExaminationSlotsQueryKey });
    },
  });
}

export function useUpdateExaminationSlotMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ slotId, values }: { slotId: string; values: ExaminationSlotFormValues }) =>
      updateAdminExaminationSlotApi(slotId, values),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminExaminationSlotsQueryKey });
    },
  });
}
