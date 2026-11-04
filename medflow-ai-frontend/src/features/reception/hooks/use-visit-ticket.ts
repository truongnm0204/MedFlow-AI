import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { fetchExaminationTicket, printExaminationTicket } from '../api/visit-ticket.api';
import type { PrintExaminationTicketRequest } from '../types/visit-ticket.types';

export interface PrintTicketMutationInput {
  idOrCode: string;
  request?: PrintExaminationTicketRequest;
}

export function useExaminationTicket(idOrCode: string | null, enabled = true) {
  return useQuery({
    queryKey: ['reception', 'visit-ticket', idOrCode],
    queryFn: () => fetchExaminationTicket(idOrCode ?? ''),
    enabled: Boolean(idOrCode && idOrCode.trim().length > 0 && enabled),
    staleTime: 30_000,
  });
}

export function usePrintExaminationTicket() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ idOrCode, request }: PrintTicketMutationInput) =>
      printExaminationTicket(idOrCode, request),
    onSuccess: (data, variables) => {
      queryClient.setQueryData(['reception', 'visit-ticket', variables.idOrCode], data);
      queryClient.setQueryData(['reception', 'visit-ticket', data.visitCode], data);
    },
  });
}
