import { useQuery } from '@tanstack/react-query';

import { fetchDispensingResult } from '../api/pharmacy.api';

export function useDispensingResult(idOrCode?: string) {
  const code = idOrCode?.trim() || '';
  return useQuery({
    queryKey: ['pharmacy', 'dispensing-result', code],
    queryFn: () => fetchDispensingResult(code),
    enabled: Boolean(code),
    staleTime: 30_000,
  });
}
