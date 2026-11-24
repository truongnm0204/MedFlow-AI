import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  confirmMedicineDispense,
  fetchDispenseConfirmContext,
  fetchPreparePrescriptionContext,
  searchPharmacyPrescriptions,
  submitPreparePrescription,
  type PrescriptionSearchQueryParams,
} from '../api/pharmacy.api';
import type { ConfirmDispensePayload } from '../types/dispense-confirm.types';
import type { PreparePrescriptionPayload } from '../types/prepare-medicine.types';

export function usePharmacyPrescriptionSearch(params?: PrescriptionSearchQueryParams) {
  return useQuery({
    queryKey: ['pharmacy', 'prescriptions', params?.query ?? '', params?.status ?? 'ALL'] as const,
    queryFn: () => searchPharmacyPrescriptions(params),
    retry: false,
  });
}

export function usePreparePrescriptionContext(idOrCode: string | undefined) {
  const code = idOrCode?.trim() || '';
  return useQuery({
    queryKey: ['pharmacy', 'prepare-context', code] as const,
    queryFn: () => fetchPreparePrescriptionContext(code),
    enabled: code.length > 0,
    retry: false,
  });
}

export function useSubmitPreparePrescription(idOrCode: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: PreparePrescriptionPayload) =>
      submitPreparePrescription(idOrCode, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['pharmacy', 'prescriptions'] });
      void queryClient.invalidateQueries({ queryKey: ['pharmacy', 'prepare-context', idOrCode] });
    },
  });
}

export function useDispenseConfirmContext(idOrCode: string | undefined) {
  const code = idOrCode?.trim() || '';
  return useQuery({
    queryKey: ['pharmacy', 'dispense-context', code] as const,
    queryFn: () => fetchDispenseConfirmContext(code),
    enabled: code.length > 0,
    retry: false,
  });
}

export function useConfirmMedicineDispense(idOrCode: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: ConfirmDispensePayload) => confirmMedicineDispense(idOrCode, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['pharmacy', 'prescriptions'] });
      void queryClient.invalidateQueries({ queryKey: ['pharmacy', 'dispense-context', idOrCode] });
    },
  });
}
